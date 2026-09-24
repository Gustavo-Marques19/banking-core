# ADR-003: Modelo contábil

- Status: aceita
- Data: 2026-09-24

## Contexto

A spec descreve o depósito como um `CREDIT 1000` sem contrapartida, o que viola a própria regra de partida dobrada (§9). Ela também funde conta do cliente e conta contábil, coloca `Status` numa transação que deveria ser imutável e não define convenção de débito e crédito.

## Decisão

### Duas entidades distintas

- `Account` (módulo Accounts): o produto do cliente. Agência, número, status (Active, Blocked, Closed), dono.
- `LedgerAccount` (módulo Ledger): a conta contábil. Código, natureza, saldo normal, moeda e, quando existir, a `Account` associada.
- Criar uma `Account` cria a `LedgerAccount` 2.1.{número} correspondente, na mesma transação.

### Plano de contas da Fase 1

| Código | Conta | Natureza | Saldo normal | Pode ficar negativa | Saldo materializado |
|---|---|---|---|---|---|
| 1.1.01 | Funding simulado | Ativo | Devedor | Sim | Não |
| 1.1.02 | Settlement no provider | Ativo | Devedor | Sim, na Fase 1 | Não |
| 2.1.{número} | Depósito do cliente | Passivo | Credor | Não | Sim |
| 2.2.01 | Clearing de transferências externas | Passivo | Credor | Não (verificado pela reconciliação) | Não |

- Funding simulado representa o dinheiro fictício que o operador injeta. Na Fase 2, depósitos reais passam a entrar por Settlement.
- Settlement fica negativo na Fase 1 porque só registra saídas. Um banco real teria esse saldo pré-financiado. É um artefato do dinheiro fictício, e a documentação diz isso em vez de esconder.
- Suspense e Capital ficaram de fora. Entram quando existir um lançamento que precise delas (por exemplo, crédito recebido do provider sem conta identificável).

### Convenção

- Cada `LedgerEntry` tem `direction` (`D` ou `C`) e `amount_minor > 0`, garantido por CHECK. Não existe valor com sinal nos lançamentos.
- Saldo de conta de saldo normal devedor = Σ D − Σ C. Saldo de conta de saldo normal credor = Σ C − Σ D.
- Do ponto de vista do banco, o dinheiro do cliente é uma dívida (passivo). Por isso um depósito **credita** a conta do cliente e uma transferência de saída **debita**.

### Estrutura

- `LedgerTransaction`: `id`, `external_id` (UNIQUE), `type`, `description`, `posted_at`, `effective_date`, `reverses_transaction_id` (UNIQUE, nulo quando não é reversão), `correlation_id`. **Sem status**: uma transação do ledger existe e está lançada, ou não existe. Status pertence à operação (Deposit, Transfer, ExternalTransfer).
- `LedgerEntry`: `id`, `transaction_id`, `ledger_account_id`, `direction`, `amount_minor`, `currency`, e, só para contas com saldo materializado, `account_sequence` e `balance_after_minor`.
- `account_balances`: uma linha por conta de cliente, com `balance_minor`, `last_sequence` e `allow_negative`. Atualizada na mesma transação dos lançamentos. `CHECK (allow_negative OR balance_minor >= 0)`.

### Por que contas de sistema não têm saldo materializado

Um `UPDATE` na linha de saldo segura um lock até o commit. Se a conta de Funding tivesse saldo materializado, todo depósito do sistema seria serializado nessa linha. O saldo das contas de sistema é calculado sob demanda (`SUM`) e verificado pela reconciliação. Pelo mesmo motivo, os lançamentos nelas não têm `account_sequence` nem `balance_after_minor`.

### Correções

Lançamento nunca é alterado nem apagado. Correção é uma nova `LedgerTransaction` com os lados invertidos e `reverses_transaction_id` apontando para a original. O UNIQUE nessa coluna impede estornar duas vezes.

## Alternativas consideradas

- **Valor com sinal** (débito positivo, crédito negativo). Deixa a soma de verificação trivial (`SUM = 0`), mas o sinal passa a depender de convenção em todo lugar que lê. Com `direction` explícita, a leitura é direta e o CHECK `amount_minor > 0` impede lançamento zerado ou negativo.
- **Saldo só por `SUM`, sem `account_balances`.** É mais puro, mas não oferece linha para travar ([ADR-004](0004-concorrencia.md)) e custa O(n) por operação.

## Consequências

- O saldo de cliente tem duas fontes que precisam concordar: `account_balances` e Σ lançamentos. A reconciliação compara as duas e alimenta a métrica `ledger.balance_mismatch`.
- Os lançamentos de cada cenário estão em [ledger/lancamentos.md](../ledger/lancamentos.md).
- Estornar uma transferência interna depende de o destinatário ainda ter saldo. Se não tiver, o estorno é recusado e vira um processo operacional (M8, maker-checker), nunca um saldo negativo.
