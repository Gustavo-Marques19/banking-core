# ADR-001: Monólito modular

- Status: aceita
- Data: 2026-09-24

## Contexto

Uma transferência precisa gravar a operação, os lançamentos no ledger e a mensagem de outbox de forma atômica. O projeto tem um desenvolvedor. A spec pede que a separação permita extrair serviços no futuro, sem exigir isso na Fase 1.

## Decisão

- Um processo ASP.NET Core e um banco Postgres. Os workers (outbox, transferência externa, reconciliação) rodam no mesmo processo como `BackgroundService`, e uma flag de configuração permite desligá-los para rodar numa instância separada.
- Projetos por camada, como na spec: `Banking.Domain`, `Banking.Application`, `Banking.Infrastructure`, `Banking.Api`, `Banking.Contracts`.
- Módulos como namespaces dentro das camadas:

  | Módulo | Conteúdo | Schema no Postgres |
  |---|---|---|
  | Accounts | Customer, Account | `accounts` |
  | Ledger | LedgerAccount, LedgerTransaction, LedgerEntry, saldos | `ledger` |
  | Payments | Deposit, Transfer, ExternalTransfer | `payments` |
  | Platform | idempotência, outbox, inbox, auditoria | `platform` |

- Cada módulo é dono das suas tabelas. Payments só grava no ledger pela interface pública do módulo Ledger, nunca direto nas tabelas.
- A integração com o provider fica em `Banking.Infrastructure`, atrás de `IBankingProvider` ([ADR-006](0006-transferencia-interna-e-externa.md)).
- Uma operação pode usar uma transação de banco que atravessa módulos. É o que garante a atomicidade de operação + lançamento + outbox, e o acoplamento é aceito de propósito.
- `Banking.ArchitectureTests` (NetArchTest) verifica:
  - `Banking.Domain` não referencia EF Core, ASP.NET Core, Npgsql nem `Banking.Infrastructure`;
  - um módulo não usa tipos internos de outro, só a interface pública.

## Alternativas consideradas

- **Microserviços.** Cada transferência viraria uma saga entre serviços e o broker passaria a ser obrigatório desde o primeiro dia. O custo é alto e não há benefício para um projeto com um desenvolvedor.
- **Um projeto por módulo** (`Banking.Ledger`, `Banking.Payments`). O compilador garantiria a fronteira, o que é melhor que um teste de arquitetura. Ficou para depois porque dobra o número de projetos e a spec já define a estrutura por camada. Revisitar se os ArchitectureTests começarem a acumular exceções.

## Consequências

- Extrair o Ledger como serviço exigiria trocar a transação compartilhada por uma saga. É uma dívida conhecida, registrada aqui.
- Deploy, debug e testes de integração ficam simples: um processo, um banco.
- A disciplina de fronteira depende dos ArchitectureTests rodarem no CI desde o M1.
