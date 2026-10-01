> Guia de padrões e boas práticas para o desenvolvimento deste mod. Parte 1 é o que o próprio projeto já estabelece (extraído do código e do `CONTRIBUTING.md` — ver análise completa em [[Analise-do-Projeto]]); parte 2 é pesquisa sobre convenções da comunidade BepInEx/Valheim; parte 3 são lacunas/recomendações específicas pro contexto deste mod (sistema de remo com estamina, marchas e item craftável — ver [[Ideação]]).

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

## 3. Recomendações para este projeto (contexto da refatoração em [[Ideação]])

Cruzando o que já existe com a pesquisa acima, pontos que valem decisão consciente antes/durante a `refatoracao-game-design`:

1. **[Decidido] Adotar o Jötunn** para a parte de item/receita do remo-arma, em vez de registrar manualmente no `ObjectDB`/`ZNetScene` via patch. Já integrado nesta branch:
   - `VikingOarsmen.csproj`: `PackageReference` pro `JotunnLib` 2.30.2 (`ExcludeAssets="runtime"` + `PrivateAssets="all"`, pra não embutir o `Jotunn.dll` no nosso próprio output — ele é instalado separadamente pelo jogador, como qualquer outro mod).
   - `Plugin.cs`: `[BepInDependency(Jotunn.Main.ModGuid)]`, sem `[NetworkCompatibility]` — essa mod continua opcional por jogador e dispensável no servidor dedicado (ver README "Multiplayer"), diferente da maioria dos mods de conteúdo baseados em Jötunn.
   - `package/manifest.json`: `ValheimModding-Jotunn-2.30.2` adicionado às dependências do Thunderstore.
   - `README.md`/`CONTRIBUTING.md`: Jötunn listado como requisito de instalação e de ambiente de desenvolvimento.
   - **Pendência conhecida:** o pacote NuGet do Jötunn declara referências a várias assemblies "`_publicized`" do jogo que este projeto não usa (ele não publiciza as assemblies, usa `HintPath` direto). Isso aparece como `warning MSB3245` no build, inofensivo enquanto não usarmos uma API do Jötunn que precise desses módulos específicos (ex: `UnityEngine.ProfilerModule`) — se isso acontecer, aí sim vale reavaliar publicizar as assemblies (ver item 5).
2. **Config atual é local-por-cliente, e isso já é relevante hoje** (cada cliente usa seu próprio `StaminaDrainAmount`/`PowerPerRower`, não só o dono do barco) — com o sistema de marchas isso tende a importar mais ainda (jogadores com configs diferentes remando o mesmo barco em "marchas" diferentes gerando thrust diferente do esperado). Se o balanceamento das marchas virar algo que o mantenedor quer igual pra todo mundo num servidor, vale considerar `ServerSync` nessa fase, não depois.
3. **Nenhum `[BepInDependency]`/`[BepInIncompatibility]` existe hoje** — correto, pois não há dependências. Se o item 1 acima for adotado (Jötunn), declarar `[BepInDependency(Jotunn.Main.ModGuid)]` no `Plugin.cs`.
4. **Logging**: o projeto usa bem `LogInfo`/`LogWarning`, mas não tem nenhum `LogDebug`. Com a complexidade nova do sistema de marchas (5 estados em vez de 2), vale considerar `LogDebug` para rastrear transições de marcha durante o desenvolvimento, sem poluir o log de produção dos usuários finais.
5. **Reflexão pontual (já em uso) deve continuar sendo a estratégia por enquanto** em vez de publicizar as assemblies inteiras: o projeto só precisa de um campo privado hoje (`m_hitWaterEffects`). Se a implementação do remo-arma precisar mexer em vários campos privados de `ItemDrop`/`Attack`/`Recipe` (prováveis candidatos para custom attack speed/hitbox do backlog), **nesse momento compensa reavaliar** adicionar uma assembly publicizada como referência de compilação (só para esses tipos), em vez de multiplicar `AccessTools.Field` espalhados.
6. **Antes de alterar `ObjectDB`/`ItemDrop`/`Attack` para o remo-arma, confirmar os nomes de campos atuais com o ILSpy já instalado** (`assembly_valheim.dll`) — a documentação pública (Jötunn, wiki) costuma ficar levemente desatualizada em relação à versão exata do jogo instalada.
7. **Manter a regra de "nunca aplicar torque"** (`ShipRowing.cs`) como invariante ao implementar o sistema de marchas: mesmo com velocidades diferentes (1/2/3/ré), o leme continua sendo o único a virar o barco, a menos que o design mude isso de propósito — vale deixar isso explícito na nova lógica de cálculo de thrust por marcha.
8. **Nenhuma violação do `?.` em objetos Unity foi encontrada no código atual** (verificado) — manter essa disciplina ao adicionar código novo (ex: ao resolver o item equipado via inventário para o novo requisito "precisa ter o remo selecionado").
