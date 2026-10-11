# Plano: som de remada

> Branch `feat/rowing-sound`, a partir da `main` já com a `feat/oar-art` mergeada (PR #7, `9969b41`). Novo recurso: som da remada sincronizado com a animação ([[Steering]] item 9). **Decisões fechadas** (seção 6) e registradas no [[Steering]] item 13; pronto para implementar.

## 1. Objetivo

Dois modos de som, decididos por barco:

| Modo | Quando | Som |
| --- | --- | --- |
| **Solo** | Só uma pessoa remando o barco | Sorteia entre `single.wav` e `single2.wav`, um por remada, nunca o mesmo mais de 2 vezes seguidas |
| **Multi** | Duas ou mais pessoas remando o mesmo barco | `multiple.wav`, um por remada |

O som acompanha o movimento: toca uma vez por ciclo de remada, num ponto fixo do ciclo. Remada mais rápida (marcha maior, `StrokeSpeed` maior) = ciclo mais curto = o som repete mais vezes. Neutro = silêncio. Ré tem som (o mesmo, na fase invertida). Todos os sons levam uma variação leve de pitch (±5%) para não soar mecânico.

## 2. Material de origem

Em `~/row-your-boat-sound/` (fora do repositório). Sons gravados/produzidos pelo maintainer, publicados sob a licença do repositório (MIT).

| Arquivo | Duração | Formato |
| --- | --- | --- |
| `single.wav` | 0,56 s | PCM 24 bit, estéreo, 44,1 kHz |
| `single2.wav` | 0,87 s | PCM 24 bit, estéreo, 44,1 kHz |
| `multiple.wav` | 0,60 s | PCM 24 bit, estéreo, 44,1 kHz |

Só os três WAV vão para o repositório (`art/sound/`); os `.RPP` do Reaper e `Backups/` ficam de fora.

Referência de ritmo: o clipe `row_stroke` tem 2,46 s. Com `StrokeSpeed` = 1 o ciclo dura ~3,3 s na marcha 1 e na ré, 2,46 s na marcha 2 e ~1,9 s na marcha 3. Todos os sons cabem com folga dentro de um ciclo, então não se sobrepõem (com `StrokeSpeed` acima de ~2 o `single2` começa a encostar no próximo; aceitável, o `AudioSource` usa `PlayOneShot`).

## 3. Como sincronizar com a remada

O ritmo já existe: `RowerAnimation` mantém a fase da remada (`_phase`, 0..1, 0 = pegada/catch), puxada para o compasso do relógio de rede (`ZNet.GetTimeSeconds`). Em regime, a fase de todo remador numa marcha **é** o compasso daquela marcha; por isso a tripulação rema em sincronia em todos os clientes sem mandar nada pela rede ([[Steering]] 1.3).

Decisão: **o som dispara quando o compasso cruza um ponto fixo do ciclo** (`SoundPhase`, constante calibrada no teste, por exemplo o momento em que a pá entra na água).

- O barco calcula o compasso direto do relógio de rede (helper `RowingBeat`, seção 4), sem ler nada do `RowerAnimation`. Assim o som não depende da animação estar rodando (remador fora da distância de renderização) e bate com ela porque os dois usam o mesmo relógio.
- Detecção de virada frame a frame: dispara quando a fase passa por `SoundPhase` entre o frame anterior e o atual (com volta no 1 → 0).
- Quando a marcha de referência muda, o compasso salta (outro ritmo); nesse frame só se guarda a fase nova, sem disparar, para não tocar um som fora de hora.
- Na ré a animação toca de trás para frente (`1 - fase`); o ponto do som vira `1 - SoundPhase`, então continua batendo com a mesma pose de pá na água.

Descartado: disparar junto com o splash (`OarVisual.TrackSplash`). Depende de a ponta da pá cruzar a superfície, o que muda com onda, barco e remo (no Drakkar já foi um problema); o som falharia algumas remadas. A fase é determinística.

## 4. Solo vs. multi: quem toca o som

Se cada remador tocasse o próprio som, numa tripulação de 4 o `multiple.wav` sairia 4 vezes empilhado. Então o som é **por barco**:

- Componente novo `RowingSound` adicionado a cada `Ship` (postfix em `Ship.Awake`, como `RaftSeat`), em **todos** os clientes, com um `AudioSource` 3D próprio num filho do barco.
- A cada frame conta os remadores ativos, do mesmo jeito que `ShipRowing.SumRowingWeight`: jogadores com `GetRowingShip == este barco`, `IsPlayerInBoat` e `GetRowingGear != Stop`. `GetRowingGear` já é a marcha efetiva (neutro ou marcha suspensa por falta de estamina = `Stop`), e vem do ZDO de cada jogador, então funciona para remotos sem nada novo na rede.
  - 0 remadores: silêncio.
  - 1 remador: modo solo, no compasso da marcha dele.
  - 2 ou mais: modo multi, no compasso da **marcha mais usada a bordo**; empate vai para a mais rápida (Full > Half > Slow > Back; Slow e Back têm o mesmo ritmo, o empate fica com Slow, remando para frente).
- Posição: o `AudioSource` vai para a média das posições dos remadores ativos (no solo, o próprio remador). Rolloff 3D: quem está longe ouve mais baixo; quem não tem o mod não ouve nada.
- Volume: o `AudioSource` sai pelo grupo de efeitos (SFX) do mixer do jogo, para respeitar o volume de efeitos das opções. Confirmado no ILSpy: `AudioMan` não guarda o grupo SFX num campo (cada prefab `sfx_*` vanilla traz o seu no `AudioSource`), mas expõe `m_masterMixer` (o volume de efeitos é o parâmetro `SfxVol` dele). Implementado: copia o `outputAudioMixerGroup` do primeiro prefab `sfx_*` do `ZNetScene` (loga o nome do grupo); se não achar, `m_masterMixer.FindMatchingGroups("SFX")`; se nada der, toca sem grupo e loga warning.
- `RowingSoundVolume` multiplica o volume por cima disso.

Para o barco saber o compasso, `StrokeRate` e `GetBeat` saem do `RowerAnimation` para um helper estático `RowingBeat`, usado pelos dois (refactor sem mudança de comportamento, commit separado).

## 5. Arquivos, carregamento e config

**Carregamento** (decidido): WAV convertidos para **16 bit mono 44,1 kHz**, embutidos na DLL como `EmbeddedResource` (`VikingOarsmen/Sounds/`), decodificados em runtime para `AudioClip.Create`.

- Não passa pelo Unity nem mexe no bundle `vikingoarsmen`. (O motivo do conflito com a `feat/oar-art` acabou com o merge do PR #7, mas continua valendo não precisar de um passo no Unity para trocar um som.)
- Mono é o formato certo para som 3D posicional; 16 bit basta e reduz o tamanho (~50–75 KB por som).
- Mesmo padrão do bundle: tudo dentro da DLL, nada muda no `package/` nem no `release.ps1`.
- Conversão: script `art/sound/convert_sounds.py` (só biblioteca padrão do Python, já que não há `ffmpeg`/`sox` no ambiente), que lê `art/sound/*.wav` e grava as versões 16 bit mono em `VikingOarsmen/Sounds/`, no mesmo espírito de `art/oar/build_oar.py`.
- Leitura: `ModSounds.cs` lê o cabeçalho RIFF (só aceita PCM 16 bit mono; qualquer outra coisa loga warning e o som fica desligado), converte as amostras para `float[]` e cria o `AudioClip` uma vez, na primeira vez que for pedido. O clipe é preenchido pelo callback de leitura do `AudioClip.Create`, não por `SetData`: uma sobrecarga de `SetData` usa `Span` do netstandard 2.1 e o compilador não resolve a chamada contra o net462 (erro CS1705).

**Config nova**, seção nova `Audio` (abre precedente para outros sons):

| Chave | Default | Descrição |
| --- | --- | --- |
| `RowingSound` | `true` | Liga/desliga o som de remada |
| `RowingSoundVolume` | `1.0` | Volume relativo ao volume de efeitos do jogo (0 a 1) |

Calibração de autor (constantes no código, não config): `SoundPhase`, distância máxima/rolloff do `AudioSource`, variação de pitch (±5%), máximo de repetições seguidas no solo (2).

**Arquivos tocados:**

| Arquivo | Mudança |
| --- | --- |
| `VikingOarsmen/RowingBeat.cs` | Novo: `StrokeRate`/`GetBeat` extraídos de `RowerAnimation` |
| `VikingOarsmen/RowerAnimation.cs` | Passa a usar `RowingBeat` |
| `VikingOarsmen/RowingSound.cs` | Novo: componente por barco, modo solo/multi, disparo por fase |
| `VikingOarsmen/ModSounds.cs` | Novo: carrega e decodifica os WAV embutidos |
| `VikingOarsmen/Sounds/*.wav` | Novo: sons convertidos (16 bit mono) |
| `VikingOarsmen/ShipRowingPatch.cs` | Adiciona `RowingSound` no `Ship.Awake` |
| `VikingOarsmen/Plugin.cs` | Seção `Audio`: `RowingSound`, `RowingSoundVolume` |
| `VikingOarsmen/VikingOarsmen.csproj` | `EmbeddedResource` dos WAV |
| `art/sound/` | WAV originais + `convert_sounds.py` |
| `README.md`, `CHANGELOG.md`, `CONTRIBUTING.md` | Feature, tabela de config, `Unreleased/Added`, tabela de layout |
| `VikingOarsmen-Documents/Steering.md` | Item 13 (feito) |

Commits previstos:
1. `refactor(rowing): extract the stroke beat from RowerAnimation`
2. `feat(rowing): play rowing sounds in step with the stroke` (código, sons, config, README, CHANGELOG, CONTRIBUTING)
3. `docs: add the rowing sound plan and steering decision` (este plano + [[Steering]] item 13), se não entrar junto no 2.

## 6. Decisões

| # | Dúvida | Decisão |
| --- | --- | --- |
| 1 | Quem conta como "remando" | Só quem está remando de fato: marcha efetiva 1, 2, 3 ou ré. Neutro ou marcha suspensa por estamina não conta e não faz som. |
| 2 | Compasso do multi com marchas diferentes | (a) Marcha mais usada a bordo; empate = a mais rápida. |
| 3 | Ré tem som | Sim, o mesmo som, na fase invertida. |
| 4 | Aleatório no solo e pitch | No máximo 2 vezes seguidas o mesmo som + variação leve de pitch (±5%), inclusive no `multiple.wav`. |
| 5 | Splash atual | Os dois tocam juntos por enquanto; decidir no teste se o som do splash sai. |
| 6 | Config | Seção nova `Audio`, com `RowingSound` e `RowingSoundVolume`. |
| 7 | Fontes no repositório | Só os WAV originais em `art/sound/`, sob a licença do repositório. Sem `.RPP` nem `Backups/`. |
| 8 | Carregamento | Embutido na DLL. |
| 9 | Base da branch | Verificado: `feat/oar-art` mergeada na `main` (PR #7). `feat/rowing-sound` avançada (fast-forward) para `9969b41`. |

## 7. Teste em jogo

Sem testes automatizados; checklist manual ([[Testes e Anotações]]):

- [ ] Solo na Raft, Karve, Longship e Drakkar: sorteia `single`/`single2`, um por remada, nunca 3 iguais seguidos, batendo com a pá na água.
- [ ] Trocar de marcha 1 → 2 → 3: o som acelera junto com a animação; neutro silencia; ré toca, na mesma pose de pá na água.
- [ ] Trocar de marcha no meio da remada: nenhum som fora de hora.
- [ ] Estamina acabando com a marcha suspensa: o som para.
- [ ] `StrokeSpeed` 0,5 e 2: o som continua no mesmo ponto da remada.
- [ ] Splash e som novo juntos: decidir se o som do splash continua (dúvida 5).
- [ ] Multiplayer, 2+ remadores no mesmo barco: só `multiple.wav`, uma vez por remada (sem empilhar), igual nos dois clientes.
- [ ] Multiplayer com marchas diferentes: segue a marcha mais usada; empate segue a mais rápida.
- [ ] Um dos dois sai do banco ou vai para neutro: volta para o modo solo.
- [ ] Jogador fora do barco / longe: ouve mais baixo ou nada; jogador remoto ouve o barco do outro.
- [ ] Volume de efeitos do jogo em 0: sem som. `RowingSound = false`: sem som. `RowingSoundVolume` 0,5: mais baixo.
- [ ] Calibrar `SoundPhase`, distância do rolloff e volume ouvindo em jogo.
