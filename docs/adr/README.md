# Architecture Decision Records

| ADR | Decisão | Status |
|---|---|---|
| [001](0001-monolito-modular.md) | Monólito modular, módulos por schema | aceita |
| [002](0002-representacao-monetaria.md) | Dinheiro em centavos (`bigint`), string decimal na API | aceita |
| [003](0003-modelo-contabil.md) | Plano de contas, convenção D/C, `Account` separado de `LedgerAccount` | aceita |
| [004](0004-concorrencia.md) | `FOR UPDATE` ordenado em `account_balances` | aceita, números pendentes (M4) |
| [005](0005-idempotencia.md) | Idempotência no Postgres, na mesma transação, com escopo por cliente | aceita |
| [006](0006-transferencia-interna-e-externa.md) | Transferência interna síncrona, externa como saga com Clearing | aceita |
| 007 | Outbox e inbox | a escrever no M5 |
| 008 | Imutabilidade garantida pelo banco | a escrever no M2 |
| 009 | Autenticação e autorização | a escrever no M3 |
| [010](0010-ambiente-de-desenvolvimento.md) | Codespaces e GitHub Actions, Vercel descartada | aceita |

Formato: contexto, decisão, alternativas consideradas, consequências. Uma ADR aceita não é editada para mudar a decisão. Se a decisão mudar, uma nova ADR a substitui e a antiga passa a "substituída por ADR-XXX".
