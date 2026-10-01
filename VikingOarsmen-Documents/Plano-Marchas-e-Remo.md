> Plano de implementação do **item 1** (refatoração do sistema de remar) + **item 1.1** (item do remo) do backlog em [[Ideação]]. Decisões de arquitetura/processo seguem [[Steering]] (binding). **Status: todas as decisões da seção 6 (D1–D6) estão fechadas — pronto para implementação.**

## 0. Escopo

**Dentro desta fase:**
- Sistema de marchas (ré / neutro / 1 / 2 / 3) controlando quanto cada remador contribui de propulsão, usando W/S.
- Estamina com custo diferente por marcha, incluindo a mecânica de "boost que corta e só reativa com estamina suficiente".
- Exigência do item do remo equipado para poder remar.
- Item do remo como arma (base no `Club`), craftável na bancada nível 1 com 6× `FineWood`, via Jötunn.

**Fora desta fase** (seções do backlog ainda vazias): item **2. Movimento** e item **3. Asset do remo** de [[Ideação]]. Assumindo que ficam para uma iteração seguinte — **avisar se não for esse o caso**.

**Versionamento:** isso é uma mudança `MAJOR` (ver `RELEASING.md`): a ativação por R deixa de existir (ver decisão D4) e passa a ser obrigatório ter o item do remo — comportamento que todo usuário precisa se adaptar.

## 1. O que o jogo já faz (verificado via ILSpy, não suposição)

Decompilei `Ship` e `Attack`/`ItemDrop.SharedData` de `assembly_valheim.dll` pra basear o plano em fatos, não em achismo (prática já travada no [[Steering]] item 5).

### 1.1 Como o leme já funciona (`Ship.cs`)
```csharp
public enum Speed { Stop, Back, Slow, Half, Full }
```
`RPC_Forward` (acionado por W) e `RPC_Backward` (acionado por S) andam **um degrau por vez**:
- Forward: `Stop→Slow→Half→Full` (e `Back→Stop`; em `Full` não faz nada)
- Backward: `Stop→Back`, `Slow→Stop`, `Half→Slow`, `Full→Half` (em `Back` não faz nada)

E o disparo é por **borda de pressionamento**, não nível contínuo:
```csharp
public void ApplyControlls(Vector3 dir) {
    bool flag  = dir.z > 0.5;   // W
    bool flag2 = dir.z < -0.5;  // S
    if (flag && !m_forwardPressed) Forward();
    if (flag2 && !m_backwardPressed) Backward();
    ...
}
```
Isso bate exatamente com o pedido do backlog ("mesma forma que o leme, exceto que não dá pra virar"). **Decisão de nomenclatura:** vou reusar os mesmos 5 nomes (`Stop/Back/Slow/Half/Full`) pro nosso enum de marcha, em vez de inventar "Neutro/Ré/1/2/3", pra manter paridade 1:1 com o conceito vanilla (Slow=marcha 1, Half=marcha 2, Full=marcha 3, Back=ré, Stop=neutro). A UI pode mostrar os nomes em português; o enum interno fica em inglês, como o resto do código.

O leme é acionado via `IDoodadController`/`Player.m_doodadController`, que só existe para quem está **pilotando** (helm). Remador sentado no banco não tem doodad controller — então não dá pra "pegar carona" nesse pipeline; vamos ler input diretamente no nosso próprio `RowingController.Update()`, do mesmo jeito que já é feito hoje para a tecla R.

**Recomendação técnica (não é decisão de design, é detalhe de implementação):** usar `ZInput.GetButtonDown("Forward")` / `ZInput.GetButtonDown("Backward")` em vez de `Input.GetKeyDown(KeyCode.W/S)` cru — é a abstração que o próprio jogo usa internamente pros binds de movimento, então respeita rebind de teclas e funciona com controle também.

### 1.2 Campos reais de arma (`Attack` e `ItemDrop.ItemData.SharedData`)

| Termo do backlog | Campo real confirmado | Observação |
| --- | --- | --- |
| Weight = 4.0 | `SharedData.m_weight` (default 1) | direto |
| Durability = 50 | `SharedData.m_maxDurability` (default 100) | direto |
| Knockback = 50 | `SharedData.m_attackForce` (default 30) — é o campo que a tooltip do jogo rotula "$item_knockback" | direto |
| Backstab = 2x | `SharedData.m_backstabBonus` (default 4) | é multiplicador — "2x" vira `2f` |
| Stamina = 12 | `Attack.m_attackStamina` (default 20) | direto |
| Adrenaline = 1 | `Attack.m_attackAdrenaline` (default 1) | já bate com o default — só deixar explícito |
| Hitbox 2x mais range | `Attack.m_attackRange` (default 1.5 na classe base) | **precisa do valor real do Club**, não do default genérico da classe — ver nota abaixo |
| Attack speed 3x mais longo | `Attack.m_speedFactor` (default 0.2 na classe base) | **sentido do multiplicador não confirmado** — ver nota abaixo |
| Type = club | `SharedData.m_itemType` + `SharedData.m_skillType` | **[D5]** iguais ao Club original (não altera) |
| Stagger | `SharedData.m_staggerMultiplier` | **[D6]** `1.5f` (o "15" do backlog era o multiplicador 1.5, não um valor absoluto) |

**Duas notas importantes que não dá pra resolver só lendo o código genérico da classe `Attack`** (os defaults ali são da classe base, não do prefab `Club` específico, que tem seus próprios valores customizados nos dados do item):
- `m_attackRange` e `m_speedFactor` do **Club de verdade** só são conhecidos lendo o prefab clonado em tempo de execução (quando o Jötunn/ObjectDB já carregou os itens vanilla). O plano é: no primeiro boot, clonar o Club, **logar os valores reais** (`Plugin.Log.LogInfo`) do Club original antes de qualquer alteração, e só então aplicar os multiplicadores (×2 no range, e o fator de ×3 na duração do golpe) sobre o valor real — nunca sobre o default genérico da tabela acima.
- Não decompilei a direção exata de `m_speedFactor` (se maior = ataque mais rápido ou mais lento). Vou confirmar isso empiricamente comparando o valor do Club com sua duração de ataque conhecida (que já é familiar de jogar) antes de aplicar a fórmula — detalhe de implementação, não bloqueia o planejamento.

## 2. Modelo de dados novo

Hoje `RowingController` guarda só o ZDOID do barco (`VikingOarsmen_RowingShip`) no ZDO do jogador — presença dele já significa "estou remando". Isso **continua** sendo a flag de "estou em modo remar" (sentado + remo equipado). Precisa de **um campo novo**:

```
VikingOarsmen_Gear : int   // (RowingGear)Stop por padrão, armazenado como o enum espelhando Ship.Speed
```

Mesma disciplina de hoje: só escreve no próprio ZDO, só quando muda.

## 3. Fluxo de ativação (substitui o toggle da tecla R)

Novo fluxo (D4 confirmado):
1. Jogador senta no banco (`IsOnBench()`, já existe) **e** tem o remo equipado como arma atual (novo check, seção 3.1).
2. Isso **ativa automaticamente** o modo remar em `Gear = Stop` (neutro) — sem precisar apertar nada.
3. W/S (borda de pressionamento) avança/recua um degrau no enum, igual ao leme (seção 1.1), só enquanto em modo remar.
4. Sair do banco, trocar de arma, morrer, ou perder o remo da mão (desequipar) **encerra** o modo remar (`Gear = Stop`, ZDOID do barco = `None`).
5. Virar helmsman continua impedindo remar (regra já existente, mantida).

### 3.1 Detectar "remo equipado"
`Humanoid.GetCurrentWeapon()` (ou equivalente) retorna o `ItemData` atual; comparamos `m_dropPrefab.name` contra o nome do nosso prefab de item novo. Isso só precisa ser checado **localmente** por cada jogador pra decidir sua própria marcha — não precisa ser sincronizado (os outros clientes só precisam saber a marcha resultante, já sincronizada via ZDO).

## 4. W/S como seletor de marcha — espelhando o leme

Replicar literalmente a tabela de transição de `RPC_Forward`/`RPC_Backward` (seção 1.1) sobre o nosso enum local, sem RPC (decisão unilateral do próprio jogador sobre a própria marcha, sincronizada depois via ZDO, igual já acontece hoje). Chamado em `RowingController.Update()`, só quando: em modo remar E não é o helmsman (continua não podendo controlar direção).

**Invariante do [[Steering]] mantida:** a marcha só altera a *magnitude* do empuxo em `ShipRowing`, nunca introduz torque/giro — isso é código novo que vou escrever já respeitando essa regra, não um risco em aberto.

## 5. Thrust por marcha em `ShipRowing`

Hoje: `target = min(MaxRowingPower, nRemadores * PowerPerRower)` — contagem simples. Novo: soma ponderada por marcha de cada remador, incluindo valores negativos pra ré:

```
contribuição(remador) = peso(marcha do remador) * PowerPerRower
target = clamp(-MaxRowingPower, MaxRowingPower, Σ contribuição)
```

Pesos exatos: ver D3 resolvida na seção 6.

## 6. Decisões

### [Decidido] D1+D2 — Mecânica de estamina
Todas as 4 marchas ativas (`Slow`, `Back`, `Half`, `Full`) usam o **mesmo mecanismo de "golpe de arma"**, com o **mesmo custo** (`StaminaDrainAmount`, default 1, como hoje) — a diferença entre marchas é **só o intervalo entre remadas** (`StaminaDrainInterval`), não o custo. Isso é, na prática, uma extensão do que `RowingController.DrainStamina` já faz hoje (checar estamina → descontar → parar se não tiver), só que agora parametrizado por marcha:

```
IntervalSlow == IntervalBack   (mesmo valor — "1 e ré consomem no mesmo tempo")
IntervalHalf  < IntervalSlow    ("2 consome mais rápido que 1")
IntervalFull  < IntervalHalf    ("3 consome mais rápido que 2")
```

**Detalhe que eu estou resolvendo por conta própria (avisar se errado):** quando o tick falha por falta de estamina, a marcha escolhida **não muda sozinha** — só aquela remada específica não produz empuxo (contribuição = 0 naquele instante), e o jogador continua tentando a cada intervalo até ter estamina de novo. Isso é mais fiel à analogia "golpe de arma que não sai" (voce não troca de arma só porque um golpe não saiu) do que forçar uma queda de marcha automática.

Defaults propostos (ajustáveis em config, como todo o resto):
- `StaminaDrainAmount` = 1 (mantém o valor atual)
- `StaminaDrainIntervalSlow` / `StaminaDrainIntervalBack` = 10s (mantém o valor atual)
- `StaminaDrainIntervalHalf` = 6s
- `StaminaDrainIntervalFull` = 3s

### [Decidido] D3 — Empuxo por marcha
Multiplicadores sobre `PowerPerRower` (que vira a base da marcha `Slow`). Defaults propostos:
- `Gear2Multiplier` (Half) = 1.5
- `Gear3Multiplier` (Full) = 2.0
- `ReverseMultiplier` (Back) = -1.0 (mesma intensidade da marcha 1, só que pra trás)

Fórmula final em `ShipRowing`:
```
peso(Stop) = 0
peso(Back) = PowerPerRower * ReverseMultiplier
peso(Slow) = PowerPerRower
peso(Half) = PowerPerRower * Gear2Multiplier
peso(Full) = PowerPerRower * Gear3Multiplier

target = clamp(-MaxRowingPower, MaxRowingPower, Σ peso(marcha do remador))
```

### [Decidido] D4 — Remoção de `RowKey`/`HoldToRow`
Confirmado: ambos removidos do `Plugin.cs`. Ativação passa a ser 100% automática (sentado + remo equipado). Bump de versão `MAJOR` necessário (`RELEASING.md`).

### [Decidido] D5 — "Type = club"
Clonar o Club mantendo `m_itemType` e `m_skillType` **iguais ao original**. Regra geral pra qualquer outra dúvida de campo que surgir durante a implementação: **seguir igual ao Club por padrão**, e só desviar depois, se algo se mostrar errado em teste — não travar a implementação tentando adivinhar antes da hora.

### [Decidido] D6 — "Stagger"
O "15" do backlog era, na real, o multiplicador **1.5** (não um valor absoluto de 15). `SharedData.m_staggerMultiplier = 1.5f`.

## 7. Config novo em `Plugin.cs` (seções `Physics`/`Gameplay`)

| Config | Default | Substitui/complementa |
| --- | --- | --- |
| `PowerPerRower` | 0.25 (mantido) | base da marcha `Slow` |
| `Gear2Multiplier` | 1.5 | novo |
| `Gear3Multiplier` | 2.0 | novo |
| `ReverseMultiplier` | -1.0 | novo |
| `MaxRowingPower` | 1.0 (mantido) | agora é um clamp simétrico (-Max..+Max) |
| `StaminaDrainAmount` | 1 (mantido) | custo por remada, compartilhado entre marchas |
| `StaminaDrainIntervalSlow` | 10s | renomeia o atual `StaminaDrainInterval` |
| `StaminaDrainIntervalHalf` | 6s | novo |
| `StaminaDrainIntervalFull` | 3s | novo |
| ~~`RowKey`~~ | — | **removido** (D4) |
| ~~`HoldToRow`~~ | — | **removido** (D4) |

## 8. Item do remo via Jötunn

1. **Clone do `Club`** via `ItemManager`/`ItemConfig` do Jötunn (`CrossReference` pro prefab vanilla `Club`), registrado em `PrefabManager.OnVanillaPrefabsAvailable` (depois que os prefabs vanilla existem).
2. **Ajustes de `SharedData`/`Attack`** conforme a tabela da seção 1.2, usando os valores reais do Club lidos em runtime (não os defaults genéricos).
3. **Visual temporário:** reaproveitar a mesma técnica que `OarModel.TryCopyRudder` já usa hoje (copiar a malha do leme de um barco de referência) — mas aplicada **uma vez**, no prefab do item (não por remador/por frame como é feito hoje para a animação). O resultado da animação de remar continua sendo o `OarModel` atual; o item na mão/hotbar/drop no chão usa essa mesma malha copiada.
4. **Ícone:** usar a geração automática de ícone do Jötunn (renderiza o prefab 3D numa textura), evitando desenhar um sprite 2D à mão — consistente com "asset temporário" do backlog.
5. **Receita:** `RecipeConfig` — estação `piece_workbench`, nível 1, custo 6× `FineWood`, quantidade produzida 1.

## 9. Checklist de teste manual (adenda ao `CONTRIBUTING.md`)

- [ ] Sem o remo equipado, sentar no banco não ativa nada (sem hint de R, já que R não existe mais).
- [ ] Com o remo equipado e sentado, modo remar ativa sozinho em neutro (sem força nenhuma).
- [ ] W avança marcha a marcha (1→2→3), S recua e entra em ré, não dá pra pular direto de 1 pra 3.
- [ ] Ré realmente empurra o barco pra trás.
- [ ] Trocar de arma ou levantar do banco encerra o modo remar imediatamente.
- [ ] Estamina se comporta conforme D1/D2 (confirmar visualmente a mecânica escolhida).
- [ ] Remo craftável na bancada nível 1 por 6 Madeira-fina, aparece com ícone e modelo (mesmo que temporário).
- [ ] Remo funciona como arma normal fora do barco (ataque, stagger, knockback) sem travar nada.
- [ ] Multiplayer: dois jogadores em marchas diferentes somam/subtraem força corretamente.
