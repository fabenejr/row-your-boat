> Discussão de caminho técnico para os itens **2. Movimento** e **3. Asset do remo** de [[Ideação]]. Ainda **não é decisão**: é uma recomendação para avaliar. O que for fechado aqui vai para [[Steering]] (seção 3), seguindo a mesma regra dos outros itens. Os fatos marcados como "verificado" foram confirmados nos arquivos do jogo instalado (ILSpy / arquivos do Valheim) em 2026-10-01.

## TL;DR

- **Asset do remo:** sim, dá para fazer de forma visual. O fluxo é **Blender → FBX → Unity (mesma versão do Valheim) → AssetBundle → carregado pelo Jötunn**. Do lado do código pouca coisa muda: o `OarItem` continua clonando o `Club` (stats, receita) e só **troca a malha** pela nossa.
- **Movimento de remar:** hoje é **100% código** (`RowerPose` sobrescreve os ossos depois do Animator). Existe caminho visual: animar no Blender em qualquer rig humanoide e o Unity **retargeta** para o personagem do Valheim. A recomendação é um **híbrido**: o corpo vem de um clipe animado (qualidade "plástica", editável visualmente) e o código continua cuidando do que precisa ser exato, que é mãos presas no remo, sincronia da tripulação pelo relógio de rede e altura da água.
- **Ferramenta que mais acelera tudo, já no curto prazo:** o mod **UnityExplorer**, que permite mexer em posição/rotação/escala de qualquer objeto **com o jogo rodando** e depois copiar os números para o código.

---

## Parte 1: Asset do remo

### 1.1 Como está hoje

- `OarItem` cria o item com Jötunn clonando o `Club` (`new CustomItem(PrefabName, "Club", config)`): o visual ainda é o do Club.
- `OarModel` monta o remo da animação **copiando o remo de leme do barco** e mede o comprimento da lâmina/cabo a partir dos bounds da malha.
- O backlog item 2 já pede para unificar os dois: usar **só o remo equipado** (o item) durante a remada.

### 1.2 Fatos que definem o pipeline

| Fato | Consequência |
| --- | --- |
| Valheim instalado roda em **Unity 6000.0.75f1** (verificado em `valheim_Data/globalgamemanagers`) | O AssetBundle **tem que ser gerado nessa versão do Unity** (mesma major; o ideal é a versão exata). Bundle de versão diferente pode simplesmente não carregar. |
| `VisEquipment` procura, dentro do prefab do item, um filho chamado **`attach`** (ou `attach_skin`) e é **ele** que vai para a mão do personagem (verificado) | Nosso prefab precisa respeitar essa estrutura: raiz = modelo largado no chão; filho `attach` = modelo na mão, com o pivô no ponto de empunhadura. |
| Jötunn já é dependência (Steering item 1) e tem utilitários de AssetBundle (`AssetUtils.LoadAssetBundle`, `LoadAssetBundleFromResources`) e o sistema de **mocks** (`JVLmock_<nome>`) para referenciar materiais/shaders/prefabs vanilla sem embuti-los | Não precisamos reimplementar carregamento nem copiar shaders do jogo; o material pode apontar para o shader/material vanilla em runtime. |

### 1.3 Passo a passo recomendado

**1. Modelar no Blender**
- Estilo Valheim: **low-poly** e textura **pequena e pintada à mão** (o jogo depende muito de iluminação/neblina; detalhe fino de textura some). Uma referência boa é o próprio remo de leme e as armas de madeira do jogo.
- Tamanho real em metros (1 unidade Blender = 1 m). Um remo de banco viking tem uns 3,5–4,5 m, mas o que importa é caber na animação: o atual usa o leme × `OarScale` 0.8.
- **Pivô (origin)** no ponto da mão de cima (empunhadura).
- Criar **Empties nomeados** como pontos de referência, que viram filhos no Unity e o código lê em vez de medir bounds:
  - `grip_top`, `grip_bottom`: onde as duas mãos seguram (hoje `RowerPose` calcula isso por porcentagem: `TopGrip`, `HandSpacing`).
  - `fulcrum`: onde o remo apoia na murada.
  - `blade_tip`: ponta da pá (para o splash e a profundidade na água).
  
  Isso troca "números mágicos ajustados no escuro" por pontos que se posicionam **visualmente** no Blender.
- Exportar **FBX** com: *Apply Scalings: FBX Units Scale*, *Forward: -Z*, *Up: Y*, *Apply Transform* marcado. É a combinação que costuma chegar no Unity sem rotação de 90° e com escala 1. Conferir no Unity mesmo assim.

**2. Montar o prefab no Unity 6000.0.75f1**
- Projeto Unity vazio (3D, Built-in Render Pipeline; o Valheim não usa URP/HDRP).
- Importar o FBX e a textura. Montar o prefab:
  ```
  VikingOarsmen_Oar        (modelo como fica largado no chão + collider)
  └── attach               (modelo na mão; pivô = empunhadura)
      ├── grip_top / grip_bottom / fulcrum / blade_tip   (empties)
  ```
- **Material:** não tentar recriar o shader do Valheim. Duas opções:
  - (a) Nomear o material `JVLmock_<material vanilla>` e deixar o Jötunn trocar pelo real ao carregar (`fixReference: true`). Bom se quiser a mesma "cara" de um item existente.
  - (b) Material simples no bundle e, no código, trocar só o `shader` pelo shader que o `Club` usa (lido do prefab clonado em runtime). Isso dá textura própria com a iluminação correta do jogo.
- Marcar o prefab com um **AssetBundle name** (ex.: `vikingoarsmen`) e gerar o bundle (script de editor com `BuildPipeline.BuildAssetBundles` ou o pacote *AssetBundle Browser*). Plataforma: o bundle Windows funciona no Proton; para Linux nativo pode ser preciso um bundle por plataforma. **A verificar no primeiro teste**, já que você joga no Linux.

**3. Levar para o mod**
- Duas opções de distribuição:
  - **Embutido na DLL** (`EmbeddedResource` no `.csproj` + `AssetUtils.LoadAssetBundleFromResources`): um arquivo só, nada muda no `package/` nem no `release.ps1`. **Recomendado.**
  - Arquivo separado na pasta do plugin: dá para trocar o bundle sem recompilar, mas exige mexer no empacotamento.
- No `OarItem`: **continuar clonando o `Club`** (mantém `ItemDrop`, `Attack`, sons, stats já ajustados) e só substituir os filhos visuais (raiz + `attach`) pelos do nosso prefab. É a menor mudança e não mexe em balanceamento.
- No `OarModel`/`OarVisual`: parar de copiar o leme do barco e usar o modelo do item (backlog item 2, "remover o remo que spawna"), lendo os empties em vez de medir bounds.

**4. Iterar visualmente dentro do jogo**
- **UnityExplorer** (mod de inspeção da comunidade): com o jogo aberto, selecionar o `attach` do remo na mão, ajustar posição/rotação/escala ao vivo e anotar os valores. Serve também para o balão da UI de marchas e para a pose do remador hoje mesmo.
- Ciclo: ajusta no Blender → exporta FBX → Unity gera bundle → `dotnet build` (já copia para `plugins`) → reinicia o jogo. Reiniciar o jogo é o passo lento. Por isso vale tirar o máximo do UnityExplorer antes de voltar ao Blender.

**Cuidados**
- **Não redistribuir assets do jogo** (malhas/texturas extraídas). Extrair com AssetRipper só para **referência local** (proporções, estilo) é prática comum; o que vai no bundle tem que ser nosso. Os mocks do Jötunn existem justamente para referenciar o vanilla sem copiá-lo.
- Atualização do Valheim que trocar a versão do Unity pode exigir **regerar o bundle**. É mais uma coisa a checar a cada update, junto com os nomes de campos (Steering item 5).

---

## Parte 2: Movimento do personagem

### 2.1 Como está hoje: procedural puro

- `RowerPose` roda em `LateUpdate`, **depois** do Animator ter aplicado a animação de "sentado", e sobrescreve ossos à mão: inclinação/torção do tronco (`LeanAngle`, `TwistAngle`, `SideLean`) e braços por IK de dois ossos para as mãos seguirem o remo.
- `OarVisual` gera o ciclo da remada (fases, `SweepAngle`, `FeatherAngle`...) e sincroniza todo mundo pelo **relógio de rede** (`GetBeat`).
- **Vantagem:** funciona igual para todos os clientes sem tocar no controlador de animação do jogo, se adapta a qualquer barco/altura da água e não tem asset nenhum.
- **Limite:** todo o "acting" (peso do corpo, ombros, cabeça, ritmo, antecipação) vira constante numérica ajustada no escuro. Chegar no "objetivo plástico" do backlog só com código é possível, mas caro e frustrante.

### 2.2 As opções

| Opção | Como funciona | Visual? | Prós | Contras |
| --- | --- | --- | --- | --- |
| **A. Só código** (atual) | Ossos sobrescritos em `LateUpdate` | Não (UnityExplorer ajuda a calibrar) | Sem assets, sincronia trivial, adapta a qualquer barco | Qualidade de movimento limitada pelo quanto dá para descrever em números |
| **B. Só clipe animado** | Animação de remada feita no Blender, tocada no Animator do jogador | Sim | Movimento com intenção, editável por quem anima | Mãos "escorregam" do remo em barcos/escalas diferentes; não reage à água |
| **C. Híbrido** (recomendado) | Clipe animado dá o corpo; código corrige mãos/remo e controla o tempo do clipe | Sim, no que importa esteticamente | Junta o melhor dos dois; o código atual de IK e sincronia é reaproveitado | Exige montar o pipeline de animação (uma vez) |

### 2.3 Por que o caminho visual é viável aqui

- O Animator do personagem é **humanoide** (Mecanim): o `RowerPose` já usa `HumanBodyBones` (verificado). Isso permite **retargeting**: uma animação feita em **qualquer** rig humanoide (Rigify, um personagem Mixamo, etc.) toca no personagem do Valheim, porque o Unity converte via Avatar humanoide.
- Então **não é preciso** o esqueleto exato do Valheim para animar. Ajuda ter as proporções parecidas (extraídas só como referência local).
- O jogo já usa **`OnAnimatorIK`** (`CharacterAnimEvent`, para os pés, verificado). Ou seja, ao menos uma camada do controlador tem *IK Pass* ligado. Isso abre a possibilidade de trocar o IK manual dos braços pelo IK nativo do Unity (`SetIKPosition(AvatarIKGoal.LeftHand, ...)`), que costuma dar resultado mais natural. **A verificar:** em qual camada está ligado e se um componente nosso no mesmo GameObject do Animator recebe o callback sem conflitar com o do jogo.

### 2.4 Passo a passo do híbrido

**1. Animar no Blender**
- Rig humanoide qualquer, de proporções próximas às do personagem do Valheim.
- Incluir um remo "fantasma" no Blender só como referência do movimento (fica fora do export do clipe; o remo real é o item).
- Fazer clipes curtos, cada um **em loop**:
  - `row_idle`: sentado segurando o remo, sem remar (backlog item 2, "no neutro só segurar"). Pode ser um loop quase parado com respiração.
  - `row_stroke`: um ciclo completo de remada (pegada → puxada → saída → recuperação). Ritmo base de 1,5 s, igual a `StrokePeriod`.
  - Depois, se quiser: variação mais rápida para marcha 3, ré.
  - Ataque do remo como arma é um item à parte (ver 2.5).
- Exportar FBX **só com a armature** e as ações.

**2. Unity (mesma 6000.0.75f1)**
- Import do FBX com *Rig: Humanoid*, conferir o mapeamento de ossos no Avatar, marcar *Loop Time*.
- Clipes vão no mesmo AssetBundle do remo.

**3. Tocar o clipe no personagem.** Duas técnicas usadas por mods:
- **`AnimatorOverrideController`**: substitui um clipe vanilla existente (ex.: o de "sentado") pelo nosso **enquanto o jogador está remando**, e devolve o original ao sair. É a técnica mais comum em mods de emote/animação customizada. Simples, mas depende de nomes de clipes vanilla (checar via ILSpy/UnityExplorer, Steering item 5).
- **Playables API** (`AnimationClipPlayable` + `AnimationLayerMixerPlayable`): toca o clipe por cima do Animator sem editar o controlador, com controle total do tempo. Mais código, mas mais isolado do controlador do jogo.
- Nos dois casos, o **tempo do clipe vem do `GetBeat()`** (`normalizedTime = fase da remada`), e não do relógio local. Assim a tripulação continua remando em sincronia sem trocar mensagem nenhuma, como hoje (Steering 1.3).

**4. Código corrige o que precisa ser exato** (reaproveitando o atual)
- `OarVisual` continua posicionando o remo na murada e acompanhando a água. Com o clipe dizendo **quando** puxar, o código diz **onde** fica a pá.
- `RowerPose` encolhe: tronco/ombros/cabeça passam a vir do clipe e ele fica só com o **IK das mãos** nos empties `grip_top`/`grip_bottom` do novo asset (manual ou nativo, ver 2.3).
- A marcha muda a **velocidade** do clipe (`speed`), e o neutro troca para `row_idle`.

### 2.5 Ataque do remo como arma

- O ataque usa `Attack.m_attackAnimation`, que é o **nome de um trigger do Animator vanilla** (ex.: os do club, machado, atgeir).
- **Primeiro passo, sem asset nenhum:** testar triggers de ataque vanilla que combinem com um remo comprido (o de arma de duas mãos tende a ler melhor que o swing de club de uma mão). Só código, troca de uma string no `OarItem`.
- Ataque com animação própria cai no mesmo mecanismo de override do 2.4 (substituir o clipe do ataque escolhido enquanto o remo estiver equipado). Deixar para depois que a remada estiver resolvida.

---

## Ordem sugerida

1. **Instalar o UnityExplorer** e usar para calibrar o que já existe (balão de marchas, pose atual). Custo zero, ganho imediato.
2. **Montar o pipeline uma vez com um asset descartável:** um cilindro com o pivô certo e os empties, levado do Blender ao Unity, gerado em bundle e carregado pelo Jötunn. Isso valida versão do Unity, plataforma do bundle (Linux/Proton) e material/shader antes de investir tempo de modelagem.
3. **Modelar o remo de verdade** e trocá-lo no `OarItem` + `OarVisual` (resolve também o "remo duplicado" do backlog item 2).
4. **Animação:** começar por `row_idle` (o mais simples e já resolve um item do backlog), depois `row_stroke` no esquema híbrido.
5. Ataque como arma: trigger vanilla primeiro, animação própria só se necessário.

## Decisões em aberto (para levar ao [[Steering]] quando fechar)

- ~~Bundle **embutido na DLL** vs arquivo separado~~ → embutido ([[Steering]] item 8).
- Técnica de animação: **override controller** vs **Playables** (recomendação: decidir depois do passo 2, olhando a estrutura real do Animator do player no UnityExplorer).
- IK das mãos: manter o **manual** do `RowerPose` vs migrar para o **IK nativo** do Unity.
- ~~Material: **mock do Jötunn** vs **shader do Club aplicado em runtime**~~ → shader do Club ([[Steering]] item 8).
