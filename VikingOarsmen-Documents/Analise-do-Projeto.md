> Análise técnica e de game design do estado atual do mod **Viking Oarsmen**, feita antes de iniciar a refatoração (branch `refatoracao-game-design`). Cobre todo o código em `VikingOarsmen/`, a documentação do repositório e as notas de design já existentes nesta pasta ([[Steering]], [[Ideação]]).

## 1. O que é o mod

**Viking Oarsmen** é um mod BepInEx + Harmony para Valheim que permite remar manualmente os barcos: o jogador senta num banco do barco, aperta **R**, e o personagem segura um remo e impulsiona a embarcação. Mais remadores = mais velocidade. Quem está no leme continua controlando a direção e não pode remar.

- GUID do plugin: `com.fabenejr.vikingoarsmen` (não pode mudar — resetaria config de todo mundo).
- Versão atual no código: `2.0.0` (`Plugin.cs`, `.csproj`, `manifest.json` — as três em sincronia, como exige `RELEASING.md`).
- `CHANGELOG.md` tem uma seção `## Unreleased` com o splash e o "remar em compasso", que **já foram mergeados na `main`** (PR #1, commit `26decea`) mas ainda não foram versionados/lançados via `scripts/release.ps1`. Ou seja: o changelog está "atrasado" em relação ao release — vale lembrar o mantenedor de rodar o release antes de cortar uma versão nova, ou isso vai se acumular com o que sai da refatoração.

## 2. Arquitetura técnica

### 2.1 Ponto de entrada — `Plugin.cs`
- `BaseUnityPlugin` padrão do BepInEx. Em `Awake()`:
  - registra todas as `ConfigEntry` (controles, física, gameplay, visual, UI);
  - aplica todos os patches Harmony da assembly (`_harmony.PatchAll()`);
  - loga confirmação de carregamento.
- `Update()` delega para `RowingController.Update()` — importante: a leitura de input acontece no `Update` do próprio plugin (frame-rate), não em `FixedUpdate`, pra não perder key presses.
- `OnDestroy()` desfaz só os patches deste mod (`UnpatchSelf`), bom cidadão para hot-reload/outros mods.

### 2.2 Injeção nos objetos do jogo — `ShipRowingPatch.cs`
Dois postfixes Harmony, sem nenhum prefix (o `CONTRIBUTING.md` já pede isso: "prefer postfixes over prefixes that skip vanilla code"):
- `Ship.Awake` → adiciona `ShipRowing` a todo barco com `ZNetView` válido (ignora ghosts de preview de construção).
- `Player.Awake` → adiciona `OarVisual` a **todo** jogador (local e remoto), pois o visual do remo precisa aparecer pra todo mundo.

### 2.3 Estado e sincronização — `RowingController.cs`
Esta é a peça central do multiplayer. Em vez de RPC, o estado "estou remando o barco X" é guardado **no ZDO do próprio jogador** (`VikingOarsmen_RowingShip`, um `ZDOID`), que o Valheim já sincroniza nativamente. Qualquer cliente pode ler o ZDO de qualquer jogador.

Fluxo por frame:
1. Resolve se o jogador *pode* remar: precisa estar sentado num banco (`IsOnBench`, verifica `Chair` no `GetAttachPoint()`), não estar no leme, e não estar morto.
2. Resolve input conforme `HoldToRow` (toggle vs. segurar).
3. Dá feedback textual quando aperta R mas não pode remar (sentado vs. no leme têm mensagens diferentes).
4. Levantar do banco, sair do barco ou morrer força `wantsToRow = false`.
5. Drena estamina em ticks (`StaminaDrainInterval`), e para de remar automaticamente se não há estamina (com o mesmo "flash" visual da barra que o jogo já usa).
6. Grava o ZDOID do barco no ZDO do jogador **só quando muda**, para não gerar tráfego de rede desnecessário.
7. `CanTakeGameplayInput()` bloqueia o toggle de remar enquanto chat/console/menu/inventário/mapa/loja estão abertos — boa prática, evita que "R" vaze pra dentro desses contextos.

**Observação de design já registrada em [[Ideação]]:** o item 2 do backlog ("Apertar R para remar ficou completamente invisível") é sobre isso — não há nenhuma affordance visual/sonora clara de que R é o botão de remar antes do jogador descobrir por tentativa e erro.

### 2.4 Física do barco — `ShipRowing.cs`
Componente por barco, só age no dono (`_nview.IsOwner()` — quem tem autoridade de física sobre aquele `ZNetView`, normalmente o host ou quem o jogo escolheu como dono):
- Conta quantos jogadores a bordo (`_ship.IsPlayerInBoat`) estão remando *aquele* barco (lê o ZDO de cada um via `RowingController.GetRowingShip`).
- `target = min(MaxRowingPower, nRemadores * PowerPerRower)`, com um ramp (`MoveTowards`) suavizando start/stop e entrada/saída de remadores.
- Empurra só na direção da proa, projetada no plano horizontal (`Vector3.ProjectOnPlane(transform.forward, Vector3.up)`), e usa `ForceMode.VelocityChange` escalado por `m_sailForceFactor` do próprio barco — ou seja, a força é proporcional à força de vela daquele tipo específico de embarcação (raft/karve/longship/drakkar têm valores diferentes nativamente), então "1.0 de MaxRowingPower" significa coisas diferentes em barcos diferentes, por design.
- Verifica flutuação (`IsFloating`, limite de 1.5 m acima da água) pra não empurrar um barco encalhado.
- **Importante para a refatoração:** não aplica torque nenhum — remar nunca vira o barco, só o leme vira. Isso é uma decisão de design explícita (ver remark no código e README) que qualquer novo sistema de "marcha" (ré, neutro, 1ª, 2ª, 3ª) do backlog precisa respeitar, a não ser que o design mude isso de propósito.

### 2.5 Visual do remo — `OarModel.cs` + `OarVisual.cs` + `RowerPose.cs`
Esse é, de longe, o trio mais complexo e com mais "tuning" fino do projeto (muitas constantes mágicas documentadas via comentário).

**`OarModel.cs`** constrói o rig do remo (`OarRig`, hierarquia `Root → Stroke → Size → mesh`):
- Reaproveita a malha do **leme de direção** (`Ship.m_rudderObject`) do próprio barco; se o barco não tem leme (ex: balsa/raft), tenta o da Karve, depois do VikingShip.
- Se nada for encontrado, cai pra um remo **procedural** feito de primitivas (cilindro + esfera + cubos), com material de madeira emprestado de uma peça de construção vanilla.
- Faz bastante matemática de transformação de espaço (matrizes) pra extrair comprimento de lâmina/cabo e reorientar a malha original (que estava deitada, de lado) pra um frame vertical padronizado.

**`OarVisual.cs`** (`MonoBehaviour` em todo `Player`) roda em `LateUpdate`, depois da Animator:
- Resolve o barco sendo remado via `RowingController.GetRowingShip`, cria/destrói o rig sob demanda.
- `FitToShip`: acha a amurada (gunwale) por **raycasting físico** contra o casco (`TryFindGunwale`/`TryHitHull`), calcula a altura do fulcro pra lâmina pegar `BladeDepth` na água, e desliza o remo nas mãos se o casco for alto demais (`MinHandle`).
- O ciclo de remada é dirigido por um **relógio de rede compartilhado** (`ZNet.instance.GetTimeSeconds()`), não por tempo local — por isso todo mundo vê a tripulação remando no mesmo compasso, mesmo sem RPC dedicado. Isso é elegante, mas significa que **qualquer mudança de velocidade de remada por "marcha" (backlog item 1) vai precisar repensar esse "beat" compartilhado**, porque hoje ele assume uma cadência única e global (`StrokeSpeed`).
- `PoseOar` resolve a inclinação do remo olhando a água debaixo da ponta da lâmina *a cada frame*, o que faz o remo acompanhar ondas e o balanço do barco — não é uma animação fixa, é resolvida proceduralmente.
- `TrackSplash` dispara o splash (`OarSplash`) quando a lâmina cruza a superfície da água entrando.

**`RowerPose.cs`** aplica, por cima da animação de "sentado" já existente (sem mexer no Animator Controller):
- IK de dois ossos (lei dos cossenos) pros braços alcançarem o cabo do remo nas duas mãos.
- Rotação do tronco (spine + chest, com split 55/45) simulando o puxão/inclinação lateral.
- Blend suave (`BlendTime`) ao começar a remar, pra não "teleportar" o personagem pra pose.

### 2.6 Splash — `OarSplash.cs`
Reaproveita o efeito de "flecha caindo na água" (`Projectile.m_hitWaterEffects`, lido por reflexão via Harmony `AccessTools` — então se o nome do campo mudar numa atualização do jogo, o splash só silenciosamente para de funcionar em vez de quebrar o mod, que é a filosofia defendida no `CONTRIBUTING.md`: "match the existing style", robustez a mudanças do jogo). Instancia só os efeitos *locais* (sem `ZNetView`) pra não duplicar via rede — cada cliente já dispara pra si mesmo.

## 3. Padrões de projeto que já existem (e que a refatoração deve seguir)

Do `CONTRIBUTING.md` + observado no código:
- **"Multiplayer first"**: só o dono do `ZNetView` altera física; cada jogador só escreve no próprio ZDO. Tudo visual tem que também funcionar pra jogadores remotos (daí `OarVisual` em todo `Player`, não só no local).
- **Postfix > prefix** nos patches Harmony — não pular código vanilla.
- **Configurações novas** sempre via `Config.Bind` com descrição clara + linha na tabela do README.
- **Nomenclatura**: campos privados `_camelCase`, estáticos privados `s_camelCase`, constantes `PascalCase`, chaves em linha própria (`.editorconfig` reforça isso).
- **Comentários em inglês**, curtos, focados no "porquê" (não no "o quê").
- **Nunca mudar o GUID do plugin.**
- Sem suíte de testes automatizados (o jogo não roda em CI) — existe um checklist manual extenso em `CONTRIBUTING.md` que cobre: carregamento do plugin, hint ao tentar remar de pé/no leme, remo não atravessa o casco, lâmina entra/sai da água corretamente, pose de duas mãos, estamina, bloqueio de input em UI, e multiplayer com 2+ jogadores.

## 4. Pipeline de build e release

- `.csproj` é SDK-style, referencia DLLs do jogo (`assembly_valheim`, `assembly_utils`, módulos `UnityEngine.*`) e do BepInEx core via `HintPath` resolvido a partir de `ValheimDir` (CLI > `Local.props` > env var > caminho padrão do Steam Windows). Todas as refs são `Private=false` (compile-time only, nunca copiadas pro output — DLLs proprietárias não podem ser redistribuídas).
- **Já ajustamos nesta sessão** (branch `feature/linux-dotnet-build`, ainda não mergeada): adicionamos `Microsoft.NETFramework.ReferenceAssemblies` pra permitir `dotnet build` fora do Windows/Visual Studio — isso ainda precisa virar PR separado.
- `scripts/release.ps1` (PowerShell — só roda em Windows ou PowerShell Core) automatiza: stampar changelog, sincronizar versão nos 3 lugares, build Release, empacotar zip pro Thunderstore, gerar notas de release. Processo de tag/push/`gh release create` é manual, documentado passo a passo em `RELEASING.md`.
- Sem CI: motivo explícito é que precisaria das assemblies proprietárias do jogo, que não podem ser redistribuídas.

## 5. Notas de design já registradas no projeto

### [[Steering]]
Documento ainda vazio de conteúdo prático — só a diretriz de "usar boas práticas de mods BepInEx para Valheim, pesquisando documentação existente". Pode ser interessante preencher com decisões já tomadas (seção 3 acima) pra virar referência rápida.

### [[Ideação]] — a mais relevante para a branch `refatoracao-game-design`
Primeira avaliação do que já foi feito, com observações de game design:
1. **Estamina pra remar faz sentido** no design do jogo — e pode inclusive furar o limite de velocidade do vento, criando um "boost com custo". Essa leitura já está implementada (ramp livre, sem cap pelo vento) mas vale confirmar se é intencional manter assim.
2. **Apertar R é invisível** (sem affordance) — ponto de polish de UX pendente.
3. **Animação ainda precisa de refino** — mesmo após o trabalho recente de splash/compasso/mãos.
4. **Ideia de remo como item craftável** obrigatório pra remar, podendo ser "estacado" (guardado) como item.

**Backlog (item 1 — Refatoração do sistema de remar do personagem)**, resumo técnico do que está pedido:
- Sistema de "marchas" como o leme do barco: até 3 velocidades + ré + neutro, usando W/S (sem controle de direção).
- Velocidade 1 e ré consomem estamina num ritmo; velocidades 2 e 3 consomem mais rápido.
- Exige o **item remo** equipado pra remar.
- Sentar no banco + remo selecionado (ou selecioná-lo) ativa o "modo remar" começando em neutro.
- Mecânica de estamina ao estilo "golpe de arma": ao zerar a estamina, o boost cai e só reativa quando a estamina recarrega o suficiente — como um ataque que "engasga" sem estamina e dispara (zerando-a) quando ela enche.
- **Item do remo (1.1):** craftado na bancada nível 1, 6×`FineWood`; implementado como arma (base no `Club`): Type=club, Weight=4.0, Durability=50, Backstab=2x, Stagger=15, Knockback=50, Stamina=12, Adrenaline=1, Attack speed=3x mais longo que o Club, hitbox 2x mais alcance. Asset temporário = reaproveitar o remo visual já usado na animação atual.
- Seções "2. Movimento" e "3. Asset do remo" do backlog ainda **estão em branco** — sem conteúdo definido ainda.

## 6. O que isso implica para a refatoração (`refatoracao-game-design`)

Pontos de atenção identificados cruzando o backlog com a arquitetura atual:

- **Modelo de estado precisa crescer.** Hoje `RowingController` guarda só um booleano implícito (remando ou não, via ZDOID do barco). O novo design pede pelo menos 5 estados (ré, neutro, 1, 2, 3) — o ZDO do jogador provavelmente precisa de um campo numérico adicional (ex: `VikingOarsmen_Gear`), e `ShipRowing.CountRowers`/cálculo de `_power` em `ShipRowing.cs` precisa virar uma soma ponderada por marcha, não uma contagem simples.
- **`OarVisual`/`RowerPose` dependem de "remando: sim/não"** pra decidir mostrar o remo. Precisam passar a depender também de "tem remo equipado" — hoje não existe nenhuma leitura de inventário/item equipado em lugar nenhum do código.
- **O remo-arma é conceito novo**: hoje o remo é puramente visual (gerado em runtime, cópia de mesh, sem `ItemDrop`/receita). Vai exigir um prefab de item de verdade (clonado do `Club`), uma receita (`CraftingStation` nível 1, custo 6 `FineWood`), e decidir se o visual atual (copiado do leme) vira o mesh desse novo item ou continua sendo um objeto puramente cosmético separado enquanto rema.
- **Beat de rede compartilhado (`GetBeat()`)** assume uma única `StrokeRate()` global. Com velocidades diferentes por marcha, cada remador pode ter uma cadência diferente — o "todo mundo rema em compasso" do v2.0.0 pode precisar ser repensado (compasso por grupo de mesma marcha? por barco? abandonar o sincronismo perfeito?).
- **Mensagens de feedback** (`"Remando!"`, hints de banco/leme) provavelmente precisam de equivalentes por marcha (ex: mostrar a marcha atual, como o HUD do leme já faz).
- **Config atual não cobre o novo design**: `PowerPerRower`/`MaxRowingPower`/`StaminaDrainAmount`/`StaminaDrainInterval` são valores únicos; o novo sistema precisa de (pelo menos) custo de estamina por marcha e, possivelmente, thrust por marcha — ou uma curva derivada de poucos parâmetros. Boa hora pra decidir isso antes de codar.
- **Nenhum teste automatizado existe** (não dá, o jogo não roda em CI) — qualquer refatoração grande vai depender inteiramente do checklist manual do `CONTRIBUTING.md`, que também vai precisar ganhar itens novos (testar as 5 marchas, craft do remo, remar sem remo equipado deve falhar, etc.).

## 7. Perguntas em aberto (vale alinhar com o mantenedor antes de codar)

1. O boost de velocidade por remar deve continuar **sem** cap pelo vento (como hoje), ou isso muda com marchas 2/3?
2. "Remo selecionado" significa *equipado na hotbar* (como uma arma normal) — nesse caso remar implicaria desembainhar o remo e isso compete com outras armas/escudo?
3. O remo-arma deve ter uso de combate real (ele é uma `Club` com stats), ou os stats de combate são só pra ele existir como item válido e o foco é mesmo remar?
4. As seções 2 (Movimento) e 3 (Asset do remo) do backlog em [[Ideação]] ainda não têm conteúdo — precisam ser definidas antes ou podem ficar pra uma segunda iteração da refatoração?
