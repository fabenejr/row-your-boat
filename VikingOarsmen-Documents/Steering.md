> Guia de padrões e boas práticas para o desenvolvimento deste mod. Parte 1 é o que o próprio projeto já estabelece (extraído do código e do `CONTRIBUTING.md` — ver análise completa em [[Analise-do-Projeto]]); parte 2 é pesquisa sobre convenções da comunidade BepInEx/Valheim; parte 3 são as decisões já fechadas especificamente pro contexto deste mod (sistema de remo com estamina, marchas e item craftável — ver [[Ideação]]), antes de iniciar a implementação.

## 1. Padrões já estabelecidos neste projeto

### 1.1 Estilo de código
- Chaves sempre em linha própria (`csharp_new_line_before_open_brace = all` no `.editorconfig`).
- `var` só quando o tipo é óbvio pela própria expressão.
- Nomenclatura: campos privados `_camelCase`; campos privados estáticos `s_camelCase`; constantes `PascalCase`.
- Comentários **em inglês**, curtos, só quando explicam o "porquê" (uma restrição escondida, um workaround, um comportamento não óbvio) — nunca o "o quê". Ver `CLAUDE.md`/instruções gerais, que seguem a mesma filosofia.
- `using` do sistema primeiro (`dotnet_sort_system_directives_first`).

### 1.2 Harmony / patches
- Classe de patches própria (`ShipRowingPatch.cs`), um `[HarmonyPatch]` por método-alvo, sempre `[HarmonyPostfix]`. **Nunca usar prefix que pule código vanilla** a menos que seja estritamente necessário — prefira adicionar componentes e lógica por cima do comportamento original.
- Um único `Harmony` instanciado no `Plugin` com o `PluginGuid` como ID, aplicado via `PatchAll()` em `Awake()`.
- Ao desligar, usar `UnpatchSelf()` (equivalente a `Unpatch` escopado ao próprio mod) — **nunca** `UnpatchAll()` sem alvo, que removeria patches de outros mods também. O projeto já faz isso certo em `Plugin.OnDestroy()`.
- Acesso a campos privados do jogo: feito por **reflexão pontual** via `HarmonyLib.AccessTools.Field` (ver `OarSplash.s_hitWaterEffectsField`), lido por nome uma vez só e cacheado. Isso é deliberado: se uma atualização do jogo renomear o campo, o recurso correspondente desliga silenciosamente (loga warning) em vez de quebrar o mod inteiro.

### 1.3 Multiplayer e sincronização ("multiplayer first")
- **Só o dono do `ZNetView` (`IsOwner()`) altera física/estado autoritativo** (ex: `ShipRowing` só aplica força no dono do barco).
- **Cada jogador só escreve no próprio ZDO** — nunca no ZDO de outro jogador ou do barco. Estado compartilhado é lido, nunca escrito, pelos outros clientes.
- Estado é propagado via **ZDO custom data** (chave de string + valor), aproveitando a sincronização nativa do Valheim, em vez de RPCs customizadas — mais simples e já resiliente a reconexão/late-join.
- Escrever no ZDO **só quando o valor muda** (ver `SetRowingShip`), para não gerar tráfego de rede desnecessário a cada frame.
- Tudo que é visual (pose, remo) precisa funcionar igual para jogadores remotos — por isso componentes como `OarVisual` são adicionados a **todo** `Player`, local ou remoto, e leem o estado sincronizado em vez de um estado só-local.
- Cadência compartilhada (ex: o compasso de remada) é derivada do **relógio de rede** (`ZNet.instance.GetTimeSeconds()`), não do tempo local de cada cliente — garante que todos vejam a mesma animação sem trocar mensagens.

### 1.4 Configuração
- Toda configuração nova do usuário passa por `Config.Bind` com descrição clara (seção, chave, default, texto explicativo) e ganha uma linha na tabela de configuração do `README.md`.
- Config é organizada em seções temáticas: `Controls`, `Physics`, `Gameplay`, `Visual`, `UI`.

### 1.5 Logging
- Um `ManualLogSource` único (`Plugin.Log`), nunca `UnityEngine.Debug.Log`.
- `LogInfo` para confirmações úteis de diagnóstico (ex: dimensões do remo calculadas, qual efeito de splash foi encontrado); `LogWarning` quando um fallback é usado (ex: nenhum leme encontrado, nenhum splash encontrado).

### 1.6 Identidade e versionamento
- GUID do plugin (`com.fabenejr.vikingoarsmen`) **nunca muda** — resetaria a config de todo mundo.
- Versão vive em 3 lugares sincronizados pelo `scripts/release.ps1`: `.csproj`, `Plugin.PluginVersion`, `package/manifest.json`. Segue SemVer (ver `RELEASING.md` para a tabela de quando subir patch/minor/major).
- Sem testes automatizados (o jogo não roda em CI); todo PR depende do checklist manual de `CONTRIBUTING.md`.

## 2. Pesquisa: boas práticas da comunidade BepInEx/Valheim

Fontes: [Valheim-Modding Wiki — Best Practices](https://github.com/Valheim-Modding/Wiki/wiki/Best-Practices), [Jötunn docs](https://valheim-modding.github.io/Jotunn/tutorials/overview.html), [Jötunn — Items](https://valheim-modding.github.io/Jotunn/tutorials/items.html), [Jötunn — Recipes](https://valheim-modding.github.io/Jotunn/tutorials/recipes.html).

- **Logging por nível com disciplina**: `LogInfo` para mensagens gerais ao usuário, `LogDebug` só para depuração (não deixar em produção), `LogWarning` para chamar atenção do usuário, `LogError` quando uma funcionalidade de fato falha. *(Este projeto já segue isso, exceto que não usa `LogDebug` — ver seção 3.)*
- **`UnpatchAll()` é perigoso**: só usar a sobrecarga que recebe o ID do próprio mod; nunca o parâmetro nulo, que desfaria patches de mods de terceiros. Também não há benefício em desfazer patches no shutdown do processo — fazer isso só adiciona risco de corromper saves em cenários incomuns.
- **Declarar dependências e incompatibilidades reais**: `[BepInDependency]` quando o mod depende de outro (ex: Jötunn); `[BepInIncompatibility]` só para conflitos que realmente quebram o jogo, não para desacordos menores de config.
- **Preferir a config nativa do BepInEx** a soluções customizadas (arquivo próprio, etc.) — assim ferramentas da comunidade (editores de config, mod managers) funcionam de graça, e o BepInEx já observa o arquivo por padrão para permitir edição em tempo real.
- **Publicizar as assemblies do jogo** (ferramenta do CabbageCrow, ou similar) para acessar membros privados do jogo com segurança de forma direta, em vez de reflexão manual espalhada pelo código — precisa ser refeito a cada atualização do jogo que mude a assembly.
- **Evitar `?.` (null-conditional) em objetos do tipo `UnityEngine.Object`** (`GameObject`, `Component`, etc.): a sobrecarga de `==` do Unity que detecta objetos "destruídos mas não nulos" é pulada pelo operador `?.`, podendo mascarar um objeto já destruído como válido. Preferir `if (obj != null)` explícito ou `TryGetComponent()`.
- **Resolver o caminho do Valheim dinamicamente no `.csproj`**, com fallback pela plataforma (Steam) — já é exatamente o que este projeto faz (`ValheimDir` com cadeia de resolução).
- **Jötunn para conteúdo, Harmony para comportamento**: a convenção da comunidade é usar o Jötunn (biblioteca de modding dedicada) para registrar itens, receitas, prefabs e localizações — evita reimplementar manualmente os pontos de patch do `ObjectDB`/`ZNetScene` — e reservar Harmony puro para alterar comportamento/lógica do jogo (o que este mod já faz muito bem para a parte de física/visual).
- **Sincronização de config entre host e clientes**: config do BepInEx, por padrão, é **local a cada cliente**, não sincronizada pela rede. Para valores que afetam o balanceamento compartilhado (ex: custo de estamina, força de propulsão), a comunidade usa o Jötunn (`ConfigManager`) ou a biblioteca **ServerSync** para forçar o mesmo valor em todos os clientes conectados a um servidor.

## 3. Decisões para este projeto (contexto da refatoração em [[Ideação]])

Cruzando o que já existe com a pesquisa da seção 2, estas são as decisões fechadas antes de começar a codar a `refatoracao-game-design`. Nada abaixo fica em aberto — qualquer mudança de rumo deve atualizar esta seção.

1. **[Decidido] Adotar o Jötunn** para a parte de item/receita do remo-arma, em vez de registrar manualmente no `ObjectDB`/`ZNetScene` via patch (e portanto nenhum `[BepInDependency]`/`[BepInIncompatibility]` era necessário antes — agora o primeiro foi adicionado). Já integrado nesta branch:
   - `VikingOarsmen.csproj`: `PackageReference` pro `JotunnLib` 2.30.2 (`ExcludeAssets="runtime"` + `PrivateAssets="all"`, pra não embutir o `Jotunn.dll` no nosso próprio output — ele é instalado separadamente pelo jogador, como qualquer outro mod).
   - `Plugin.cs`: `[BepInDependency(Jotunn.Main.ModGuid)]`, sem `[NetworkCompatibility]` — essa mod continua opcional por jogador e dispensável no servidor dedicado (ver README "Multiplayer"), diferente da maioria dos mods de conteúdo baseados em Jötunn.
   - `package/manifest.json`: `ValheimModding-Jotunn-2.30.2` adicionado às dependências do Thunderstore.
   - `README.md`/`CONTRIBUTING.md`: Jötunn listado como requisito de instalação e de ambiente de desenvolvimento.
   - **Pendência conhecida:** o pacote NuGet do Jötunn declara referências a várias assemblies "`_publicized`" do jogo que este projeto não usa (ele não publiciza as assemblies, usa `HintPath` direto). Isso aparece como `warning MSB3245` no build, inofensivo enquanto não usarmos uma API do Jötunn que precise desses módulos específicos (ex: `UnityEngine.ProfilerModule`) — se isso acontecer, aí sim vale reavaliar publicizar as assemblies (item 5).
2. **[Decidido] Não adotar ServerSync agora.** Cada jogador poder estar numa marcha diferente remando o mesmo barco **é intencional** (não é um problema de sincronização a resolver — é o próprio design: a soma ponderada por marcha de cada remador *é* o esperado). O risco teórico de um jogador alterar localmente `PowerPerRower`/`StaminaDrainAmount` pra ganhar vantagem não é visto como impeditivo suficiente para justificar a dependência agora. Revisitar só se isso virar um problema reportado na prática (ex: reclamação de servidor sobre desbalanceamento).
3. **[Decidido] Adotar `LogDebug`** para rastrear transições de marcha e outros estados internos do novo sistema durante o desenvolvimento, mantendo `LogInfo`/`LogWarning` como hoje para o que é relevante ao usuário final.
4. **[Mantido como está, sem necessidade de decisão agora] Reflexão pontual continua sendo a estratégia** (via `AccessTools.Field`, como em `OarSplash`) em vez de publicizar as assemblies inteiras. Só reavaliar publicizer **se** a implementação do remo-arma precisar mexer em muitos campos privados de `ItemDrop`/`Attack`/`Recipe` de uma vez — não é uma decisão a tomar agora, é um gatilho a observar durante a implementação.
5. **[Adotado como prática de workflow] Confirmar nomes de campos reais com o ILSpy** (já instalado, aponta pra `assembly_valheim.dll`) antes de qualquer patch em `ObjectDB`/`ItemDrop`/`Attack` para o remo-arma — a documentação pública (Jötunn, wiki da comunidade) costuma ficar um pouco atrás da versão exata do jogo instalada.
6. **[Decidido / invariante travada] Remar nunca aplica torque.** O leme continua sendo o único jeito de virar o barco, mesmo com marchas diferentes (1/2/3/ré/neutro) — a nova lógica de thrust por marcha só pode mudar a magnitude da força ao longo da proa, nunca introduzir rotação. Qualquer mudança nisso exigiria uma decisão de design nova e explícita, não um efeito colateral da refatoração.
7. **[Adotado como prática] Continuar evitando `?.` em objetos `UnityEngine.Object`** (nenhuma ocorrência encontrada no código atual) — vale atenção especial ao escrever a leitura do item equipado via inventário para o requisito "precisa ter o remo selecionado para remar".
8. **[Decidido] Asset do remo: bundle embutido na DLL e shader do jogo aplicado em runtime** (fecha duas das decisões em aberto de [[Pipeline-Asset-e-Animacao]]).
   - O AssetBundle `vikingoarsmen` (gerado no Unity 6000.0.75f1) fica em `VikingOarsmen/AssetBundles/` como `EmbeddedResource` e é carregado com `AssetUtils.LoadAssetBundleFromResources`. Nada muda no `package/` nem no `release.ps1`.
   - Material: opção (b). O material do bundle só carrega a textura; em runtime o `OarItem` troca o shader pelo do modelo do `Club` (`Custom/Creature`). Motivo: o shader que vai no bundle é compilado só para a API gráfica da plataforma do build (Vulkan/OpenGL no Linux, D3D11 no Windows), enquanto o shader do jogo vale em qualquer plataforma.
   - O `OarItem` mantém a hierarquia do `Club` clonado (`attach/model`, `attach/collider`, `attach/equiped/trail`, `UpgraderGlow`) e só troca malha, material, collider e trail, além de copiar os empties (`grip_top`, `grip_bottom`, `fulcrum`, `blade_tip`) para dentro do `attach`.
   - **A verificar:** se um bundle gerado só para Linux carrega no Windows. Até lá, o bundle embutido é o de Linux.
9. **[Decidido] Animação de remada: clipes próprios tocados via Playables** (fecha a decisão "override controller vs Playables" de [[Pipeline-Asset-e-Animacao]]).
   - Clipes `row_idle` e `row_stroke` animados no Blender sobre o esqueleto do próprio jogo (extraído localmente; nada do jogo vai no repositório), com bake do IK e do rig do remo, importados no Unity como Humanoid e copiados para `.anim` no bundle `vikingoarsmen`.
   - `RowerAnimation` cria um `PlayableGraph` com saída própria no `Animator` do jogador: com peso 1 ele sobrepõe o controlador vanilla, que continua rodando por baixo e volta sozinho quando o peso cai. Não depende de nomes de estados/clipes vanilla (diferente do override controller).
   - Tempo da remada vem do relógio de rede (`ZNet.GetTimeSeconds`), como a remada antiga: tripulação sincronizada sem RPC. Neutro = `row_idle`; marchas = `row_stroke` (ré toca de trás para frente), ritmo por marcha × `StrokeSpeed`.
   - Dois lados: os clipes como animados remam por cima do ombro esquerdo do remador (boreste, olhando para a popa); para bombordo há `row_idle_mirror`/`row_stroke_mirror`, espelhados no Blender osso a osso (o flag de mirror do importador do Unity não trocou as mãos no teste). O lado é escolhido pela posição do assento no barco (x do assento < 0 = bombordo). No lado espelhado o remo passa para a mão esquerda (`VisEquipment.m_leftHand`) enquanto rema: ele fica sempre preso à mão da ponta do cabo, a única cuja pose o retarget Humanoid reproduz com precisão (preso à mão de apoio, o erro do pulso amplificado na pá jogava o remo para dentro do barco). Como `LeftHand_Attach` é o espelho de `RightHand_Attach` e o remo é simétrico na largura da pá, a pegada de remada é a mesma nas duas mãos.
   - Pegada de remada diferente da de arma: ao remar, o remo na mão vai para 0,08 m da ponta do cabo e gira −90° no eixo do cabo (medido do rig no Blender); volta à pegada de arma ao sair.

10. **[Decidido] Ajuste do remador por barco (`ShipFit`), só no visual.** Cada tipo de barco tem bancos em lugares diferentes, então uma tabela no código (chave = nome do prefab do barco) guarda quanto o corpo desliza em direção à amurada (`Outboard`, metros) e quanto o tronco se inclina sobre ela (`Lean`, graus). O attachpoint do banco nunca é alterado: o deslocamento vai no `Visual` e a inclinação gira o osso `Spine` (Humanoid) depois do Animator, então o quadril fica no banco e tronco, braços e remo inclinam juntos. Barco fora da tabela = sem ajuste (como o Karve). Valores são calibração de autor, não config do usuário.

11. **[Decidido] Banco extra na Raft e mastro/proa fora da remada.**
   - A Raft não tem banco: o mod clona o `sit_box (1)` do VikingShip (caixa, collider e `Chair` como o jogo os tem) para dentro de `interactive` de cada Raft quando ela nasce (postfix de `Ship.Awake`, com guarda contra duplicar), à frente do mastro do lado esquerdo. Não dá para alterar o prefab: ele vem dos asset bundles do jogo e o Unity recusa criar objetos dentro dele ("Cannot instantiate objects with a parent which is persistent"). Só quem tem o mod vê e usa o banco; o estado de rede da Raft não muda.
   - Segurar no mastro ou na proa também é um `Chair` (`m_name = "$ship_holdfast"`, animações `attach_mast`/`attach_dragon`); esses nunca contam como banco de remo. Bancos de verdade são `$piece_stool`.

12. **[Decidido] Drakkar Oar: segundo remo, obrigatório no Drakkar.** Os bancos do Drakkar ficam altos demais para o remo comum alcançar a água, então há um segundo item, `VikingOarsmen_DrakkarOar` ("Drakkar Oar"): Workbench nível 2, 16× `YggdrasilWood` + 8× `Resin`, status próprios: dano blunt 70 (90 no nível máximo de qualidade, distribuído igualmente pelas melhorias), peso 8, durabilidade 90, knockback 300, stamina por ataque 15, alcance 6 m; o resto (backstab, stagger, adrenalina, velocidade do ataque) igual ao remo comum. No Drakkar (`VikingShip_Ashlands`) só ele rema (o remo comum mostra uma dica para equipá-lo); nos outros barcos os dois remam.
   - Modelo: o mesmo `art/oar/build_oar.py`, variante `drakkar` → `art/drakkar_oar/`: cabo 2× e pá 1,2× (4,85 m no total), mesma espessura, pegadas iguais (a animação segura no mesmo lugar). Textura gerada por código com a paleta medida da textura do casco do Drakkar (madeira escura com veios, ferro com filete ocre, escamas verdes no pescoço da pá, tábuas claras); nenhum pixel do jogo no repositório.
   - Os dois prefabs vão no mesmo bundle `vikingoarsmen`; `OarItem` descreve cada remo (`OarKind`) e lê a ponta da pá do próprio modelo.

