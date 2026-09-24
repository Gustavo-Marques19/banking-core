# Direção de design: app do cliente

Respostas do dono do projeto, transcritas em 2026-09-24. O que foi derivado dessas respostas (valores de cor, dials) está marcado como tal.

## Identidade

- **Nome: Banking Core.** Logo em texto (wordmark), nunca gerado.
- Público: clientes do banco fictício, no celular e no desktop por igual.

## Personalidade

- **Fintech calma**, da mesma família do backoffice: leve, com espaço, tipografia marcante e um único acento.
- O saldo e a próxima ação (transferir, ver extrato) vêm primeiro. Dinheiro nunca é decoração: todo valor na tela é real.

## Tema

- **Só claro.** Distingue do backoffice (só escuro) à primeira vista: ninguém confunde o app do cliente com a ferramenta do operador.

## Cor

Acento: **o mesmo azul-petróleo do backoffice**, uma marca para os dois produtos. Valores derivados para fundo claro, com contraste conferido (WCAG AA) sobre a superfície mais escura onde texto aparece (`surface-sunken`):

| Token | Valor | Uso | Contraste |
|---|---|---|---|
| `bg` | `#F6F8F8` | fundo | |
| `surface` | `#FFFFFF` | painéis | |
| `surface-sunken` | `#EEF2F3` | áreas rebaixadas, linhas alternadas | |
| `border` | `#D5DDE0` | divisórias | |
| `text` | `#16212A` | texto principal | 15,33:1 sobre `bg` |
| `text-muted` | `#56656D` | rótulos, metadados | 5,36:1 |
| `accent` | `#1F6F76` | ação principal (texto branco: 5,84:1), links, foco | 5,18:1 como texto |

Cores funcionais, só para estado:

| Token | Valor | Estado | Contraste |
|---|---|---|---|
| `success` | `#1E7A4F` | concluída, crédito | 4,71:1 |
| `warning` | `#8A5A00` | em processamento | 5,26:1 |
| `danger` | `#B3261E` | recusada, débito falhou | 5,80:1 |

Regras:
- O acento aparece na ação principal de cada tela, em links e no foco.
- Estado e direção do dinheiro nunca só por cor: "recebida", "enviada", sinal de menos em débito.

## Tipografia

- **IBM Plex Sans**, a mesma do backoffice. Algarismos tabulares em todos os valores.
- Saldo em destaque, no maior tamanho da tela.

## Densidade

- **Confortável.**

## Movimento

- **Só hover e foco.**

## Dials

Derivados: `Dial: ENERGY 2 / RHYTHM 2 / MOTION 1`

Design Read: *app bancário do cliente, celular e desktop, fintech calma em tema claro, dial ENERGY 2 / RHYTHM 2 / MOTION 1.*
