# ADR-008: Imutabilidade e consistência garantidas pelo banco

- Status: aceita
- Data: 2026-09-24
- Refina: [ADR-004](0004-concorrencia.md) (quem atualiza o saldo e como a aplicação trava a linha)

## Contexto

O critério do M2 é: "é impossível, mesmo via SQL cru com a role `app`, criar um ledger inconsistente". Com a ADR-004 como estava escrita, a aplicação fazia `SELECT ... FOR UPDATE` e depois `UPDATE` em `account_balances`. Para isso, a role `banking_app` precisaria de `UPDATE` na tabela de saldos, e qualquer um com essa credencial poderia escrever o saldo que quisesse.

## Decisão

O banco é a autoridade sobre saldo, sequência e balanceamento. A aplicação só insere lançamentos.

| Regra | Mecanismo |
|---|---|
| Σ D = Σ C por moeda, mínimo de 2 lançamentos | constraint trigger `DEFERRABLE INITIALLY DEFERRED` em `ledger_transactions` e `ledger_entries`, verificada no commit |
| Valor positivo | `CHECK (amount_minor > 0)` |
| Lançamento na moeda da conta | FK composta `(ledger_account_id, currency)` → `ledger_accounts(id, currency)` |
| Saldo, `account_sequence` e `balance_after_minor` | trigger `BEFORE INSERT` em `ledger_entries` (`ledger.apply_entry`, `SECURITY DEFINER`). Valores enviados pela aplicação são descartados |
| Conta de cliente nunca negativa | `CHECK (allow_negative OR balance_minor >= 0)` em `account_balances`, disparado pelo `UPDATE` do trigger |
| Lançamento só entra na transação de banco que criou a transação contábil | coluna `created_xact` preenchida por trigger com `pg_current_xact_id()` e conferida em cada lançamento |
| Append-only | triggers rejeitam `UPDATE`, `DELETE` e `TRUNCATE` em `ledger_accounts`, `ledger_transactions` e `ledger_entries`, inclusive para o dono das tabelas |
| Saldo só muda por lançamento | trigger em `account_balances` zera o saldo na criação e rejeita `UPDATE` direto (só aceita o que vem de dentro de `apply_entry`) e `DELETE` |
| Linha de saldo criada junto com a conta | trigger `AFTER INSERT` em `ledger_accounts` |
| Conta de sistema só por migration | o mesmo trigger rejeita conta sem `account_id` quando a sessão é `banking_app` |
| Regras de conta de cliente | `CHECK`: passivo, saldo normal credor, sem saldo negativo, com saldo materializado |
| Estorno único | `UNIQUE (reverses_transaction_id)` |
| Operação lançada uma vez | `UNIQUE (external_id)` |

Permissões da role `banking_app` no schema `ledger`:

- `SELECT` e `INSERT` em `ledger_accounts`, `ledger_transactions` e `ledger_entries`;
- só `SELECT` em `account_balances`;
- `EXECUTE` em `ledger.lock_balances(uuid[])`.

### Lock sem permissão de UPDATE

`SELECT ... FOR UPDATE` exige privilégio de `UPDATE`. Para a aplicação travar as linhas de saldo sem esse privilégio, existe a função `ledger.lock_balances(uuid[])`, `SECURITY DEFINER`, que faz o `SELECT ... ORDER BY ledger_account_id FOR UPDATE` descrito na ADR-004. Locks de linha valem até o fim da transação, então continuam segurados depois que a função retorna.

## Alternativas consideradas

- **Aplicação calcula e grava o saldo** (desenho original da ADR-004). Mais simples, mas a credencial da aplicação passa a poder escrever qualquer saldo. Descartado.
- **Só calcular saldo por `SUM`, sem tabela de saldos.** Elimina o problema de escrita, mas volta o custo O(n) e a falta de linha para travar.
- **Advisory locks em vez de `FOR UPDATE`.** Não exigem privilégio, mas o lock deixa de estar ligado à linha que ele protege, e uma colisão de hash serializa contas sem relação.

## Consequências

- A regra de débito e crédito existe em dois lugares: no domínio (`LedgerBalance`, para decidir e responder `INSUFFICIENT_FUNDS`) e no trigger (para garantir). Os testes de integração cobrem os dois.
- Um superusuário ainda consegue desligar triggers (`session_replication_role = replica`). A defesa contra isso é detecção: reconciliação e trilha de auditoria com hash encadeado (M7).
- Mudar regras do ledger exige migration, não só deploy de código. É o comportamento desejado para um ledger.
