# Direção de design: backoffice

Respostas do dono do projeto, transcritas em 2026-09-24. O que foi derivado dessas respostas (valores de cor, fonte específica, dials) está marcado como tal e pode ser trocado sem nova rodada de perguntas.

## Identidade

- **Ferramenta interna, sem marca.** Nome funcional: "Banking Core · Backoffice". Sem logo; o nome aparece em texto.
- Público: operadores e administradores do banco fictício. Usam por muitas horas, decidindo aprovações e resolvendo pendências.

## Personalidade

- **Fintech calma:** moderna e leve, com espaço, tipografia marcante e um único acento de cor. Nada de decoração que não informe.
- A tela existe para uma decisão. O que o operador precisa decidir vem primeiro; o resto espera.

## Tema

- **Só escuro.** Motivo: uso prolongado por operadores (fila de aprovações, revisão manual, reconciliação). Não há alternância de tema.

## Cor

Acento escolhido: **azul-petróleo**. Valores derivados, com contraste conferido (WCAG AA) sobre a superfície elevada, que é o fundo mais claro onde texto aparece:

| Token | Valor | Uso | Contraste |
|---|---|---|---|
| `bg` | `#0E1417` | fundo da aplicação | |
| `surface` | `#151D21` | painéis | |
| `surface-raised` | `#1C262B` | linhas em destaque, diálogos | |
| `border` | `#2A363C` | divisórias | |
| `text` | `#E4EAEC` | texto principal | 12,69:1 |
| `text-muted` | `#9BA9AF` | rótulos, metadados | 6,38:1 |
| `accent` | `#1F6F76` | preenchimento da ação principal (texto branco: 5,84:1) | |
| `accent-text` | `#5FB7BD` | links, foco, valor em destaque | 6,61:1 |

Cores funcionais, só para estado (não contam como paleta):

| Token | Valor | Estado | Contraste |
|---|---|---|---|
| `success` | `#4FB286` | concluído | 5,91:1 |
| `warning` | `#D9A441` | pendente, esperando decisão | 6,86:1 |
| `danger` | `#E0706A` | falhou, recusado, adulteração | 4,92:1 |

Regras:
- O acento aparece na ação principal de cada tela e no foco. Não em ícones, bordas e fundos ao mesmo tempo.
- Estado nunca é comunicado só por cor: sempre com texto ("pendente", "recusado").

## Tipografia

- **Sans humanista.** Fonte derivada: **IBM Plex Sans**, porque é humanista, foi desenhada para interfaces técnicas e tem algarismos tabulares, que alinham valores em colunas de dinheiro.
- **IBM Plex Mono** só para identificadores (ids, trace id, hash), nunca para títulos.
- Valores monetários com algarismos tabulares, alinhados à direita.

## Densidade

- **Confortável:** tabelas com respiro, menos linhas por tela, leitura tranquila.

## Movimento

- **Só hover e foco.** Nada anima sozinho, nada em loop.

## Dials

Derivados das respostas acima:

`Dial: ENERGY 2 / RHYTHM 2 / MOTION 1`

- ENERGY 2: fintech calma, com tipografia marcante e um acento, sem ser austera.
- RHYTHM 2: telas consistentes entre si, com quebras onde a decisão pede (uma fila não tem a mesma forma de uma trilha de auditoria).
- MOTION 1: só hover e foco.

Design Read: *backoffice interno de banco para operadores, linguagem fintech calma em tema escuro, dial ENERGY 2 / RHYTHM 2 / MOTION 1.*
