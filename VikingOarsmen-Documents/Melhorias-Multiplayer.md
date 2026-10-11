# Melhorias de multiplayer

> Anotações para melhorar a remada em tripulação, levantadas na revisão do [[Plano-Som-de-Remada]]. Ainda **não planejado**: vira um patch próprio, em uma branch separada, depois do som. As decisões fechadas entram no [[Steering]].

## 1. Bloquear a direção contrária

Quando um jogador já está remando o barco numa direção (frente ou ré), outro jogador **não pode remar na direção contrária**. Ao tentar, aparece a mensagem "Impossível remar nessa direção" e a marcha não é aplicada.

- Remar na mesma direção continua livre, inclusive em marchas diferentes (1 e 3 juntos para frente), como já decidido no [[Steering]] item 2.
- Vantagem extra: frente e ré nunca se misturam no mesmo barco, o que também simplifica o compasso do som multi ([[Steering]] item 13).

Dúvidas a fechar:
- Quem é bloqueado é sempre quem chegou depois? Se o primeiro remador for para neutro ou sair do banco, o segundo pode inverter?
- E se o primeiro quiser inverter enquanto o segundo ainda rema na direção atual: também é bloqueado?
- A checagem roda só no cliente de quem troca de marcha, lendo as marchas dos outros no ZDO (cada jogador só escreve no próprio ZDO, [[Steering]] 1.3). Dois jogadores trocando no mesmo instante podem passar os dois; aceitável, ou precisa de árbitro (dono do barco)?
- A mensagem usa `MessageHud` (centro da tela, como a dica do Drakkar Oar)? Texto final e localização.

## 2. Animação casada ao começar a remar

Ao começar a remar, se já há um jogador remando na mesma direção e na mesma marcha, a animação deve entrar já casada com a dele.

Em regime isso já acontece: todos seguem o mesmo compasso, calculado do relógio de rede. O descompasso visível é na **entrada**:
- ao sair do neutro (ou trocar de marcha), a fase parte de onde estava e só alcança o compasso em ~`SyncTime` (0,5 s);
- o `RowerAnimation.Create` semeia a fase com o ritmo da marcha 2, qualquer que seja a marcha.

Correção provável: ao começar a remar ou trocar de marcha, saltar a fase direto para o compasso da marcha (usando o `RowingBeat` que o plano do som extrai), mantendo o deslize suave só para pequenos desvios. Confirmar em jogo se o descompasso observado é esse.

## 3. Teste em jogo

- [ ] Dois jogadores: um rema para frente, o outro tenta ré → mensagem e marcha bloqueada (e o inverso).
- [ ] Dois jogadores para frente em marchas diferentes → os dois remam.
- [ ] Segundo jogador entra na mesma marcha → animação já casada desde a primeira remada, nos dois clientes.
