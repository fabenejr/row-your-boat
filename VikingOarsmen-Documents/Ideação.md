Com base na primeira avaliação do que já foi feito do mod, observações de game design:

### Analise 1
1. Uso de estamina para remar, o que faz todo sentido em relação ao game design do jogo em si, podendo inclusive quebrar a regra de limitação de velocidade máxima do barco conforme o vento, dessa forma criando um boost mas com um custo.
2. Apertar R para remar ficou completamente "invisível"
3. A animação de remar ainda irá precisar de bastante refino.
4. Ideia de ser necessário fabricar um remo e te-lo no inventário para poder remar, podendo estacar como item, fabricação a custo somente de madeira.

# Backlog
## 1. Refatoração do sistema de remar do personagem
- Seleção de até 3 velocidades e uma de ré e o "neutro" (sem movimento), da mesma forma que é o controle do barco, com a exceção que não é possível selecionar a direção do barco.
	- Usa-se os mesmos controles, W e S
- A velocidade 1 e a ré usam o mesmo tanto de estamina
- A velocidade 2 e 3 usam estamina mais rápido
- Necessário o item do remo para remar
- Ao sentar no banco do barco, e selecionar o remo (ou já estar com o remo selecionado) ativa o modo de remar, começando no "neutro"
- Ao acabar a estamina, o "boost" de velocidade se encerra até recarregar o necessário de estamina para ativar novamente, em uma mecânica tipo sendo clicado para dar um golpe mas não tem estamina, mas quando tem estamina suficiente o golpe sai e é zerada a estamina novamente.
### 1.1 Item do remo
- Feito somente de finewood, usando 6 unidades de finewood
- Feito na bancada nível 1
- Usar o asset utilizado no momento na animação como asset do novo item, de forma temporária
- O item é uma arma, pode se basear no item "Club"
	- Type = club
	- Weight = 4.0
	- Durability = 50
	- Backstab = 2x
	- Stagger = 15
	- Knockback = 50
	- Stamina = 12
	- Adrenaline = 1
	- Attack speed = 3x mais longo que do club
	- Hitbox com 2x mais range

## 2. Movimento
- O personagem está ficando com o remo na mão e mais o remo que "spawna" ao iniciar o ato de remar. Remover o remo que antes aparecia ao iniciar, vamos usar somente o remo equipado que é a arma
- Ao ativar o modo do remo, no neutro, o personagem está remando. Ajustar para somente segurar o remo e ficar sentado mas sem fazer movimento de remar.
- Retrabalhar o movimento de remada com objetivo plástico
- Movimento de ataque do remo como arma
## 3. Asset do remo
- Criação de um asset de remo medieval viking para o item
- Aplicar e ajustar ao movimento de remada
