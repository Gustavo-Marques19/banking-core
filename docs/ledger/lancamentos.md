# Lançamentos por cenário

Referência para o M2, o M4 e o M6. Cada cenário mostra o que é gravado no ledger e o que não é. As decisões por trás estão na [ADR-003](../adr/0003-modelo-contabil.md) e na [ADR-006](../adr/0006-transferencia-interna-e-externa.md).

Contas usadas nos exemplos:

| Código | Conta | Saldo normal |
|---|---|---|
| 1.1.01 | Funding simulado | Devedor |
| 1.1.02 | Settlement no provider | Devedor |
| 2.1.0001 | Cliente A | Credor |
| 2.1.0002 | Cliente B | Credor |
| 2.2.01 | Clearing de transferências externas | Credor |

Valores em reais para facilitar a leitura. No banco, tudo é `amount_minor` em centavos.

## 1. Depósito do operador (R$ 1.000 para A)

`external_id = "{depositId}"`

| Conta | D | C |
|---|---|---|
| 1.1.01 Funding simulado | 1.000 | |
| 2.1.0001 Cliente A | | 1.000 |

Saldo de A: 1.000. Só a linha de saldo de A é travada. Funding não tem saldo materializado.

## 2. Transferência interna concluída (A → B, R$ 100)

`external_id = "{transferId}"`

| Conta | D | C |
|---|---|---|
| 2.1.0001 Cliente A | 100 | |
| 2.1.0002 Cliente B | | 100 |

Travadas as linhas de A e B, em ordem de id. Saldos: A 900, B 100.

## 3. Transferência interna rejeitada

Nenhum lançamento. É gravada a operação com status `REJECTED` e o motivo (`INSUFFICIENT_FUNDS`, `ACCOUNT_BLOCKED`, `LIMIT_EXCEEDED`), junto com a resposta de idempotência.

## 4. Transferência externa: reserva (Tx1, A envia R$ 100)

`external_id = "external-transfer:{id}:reservation"`, estado `CREATED`

| Conta | D | C |
|---|---|---|
| 2.1.0001 Cliente A | 100 | |
| 2.2.01 Clearing | | 100 |

O dinheiro sai do saldo de A na hora. Clearing passa a dever R$ 100 a alguém fora do banco.

## 5. Transferência externa concluída

`external_id = "external-transfer:{id}:resolution"`, estado `COMPLETED`

| Conta | D | C |
|---|---|---|
| 2.2.01 Clearing | 100 | |
| 1.1.02 Settlement | | 100 |

Clearing volta a zero para essa operação. Settlement (ativo) diminui: o dinheiro saiu da nossa conta no provider.

## 6. Transferência externa recusada ou cancelada

`external_id = "external-transfer:{id}:resolution"`, `reverses_transaction_id` = Tx1, estado `FAILED` ou `CANCELLED`

| Conta | D | C |
|---|---|---|
| 2.2.01 Clearing | 100 | |
| 2.1.0001 Cliente A | | 100 |

O saldo de A volta ao valor anterior. A Tx1 continua no ledger, sem alteração.

## 7. Transferência externa em UNKNOWN

Nenhum lançamento novo. Os R$ 100 continuam em Clearing até a reconciliação decidir entre o cenário 5 e o 6.

## 8. Timeout, mas o provider processou

1. Tx1 como no cenário 4.
2. O worker grava `UNKNOWN`, chama o provider e recebe timeout. O estado continua `UNKNOWN`.
3. A reconciliação consulta o status e recebe `Completed`.
4. Lançamento do cenário 5, uma única vez.

Se o webhook chegar ao mesmo tempo que a reconciliação, as duas tentam gravar `"external-transfer:{id}:resolution"`. O UNIQUE em `external_id` deixa só uma passar.

## 9. Estorno de transferência interna (M8)

`reverses_transaction_id` = transação original

| Conta | D | C |
|---|---|---|
| 2.1.0002 Cliente B | 100 | |
| 2.1.0001 Cliente A | | 100 |

Exige saldo em B. Se B já gastou o dinheiro, o estorno é recusado e vira processo operacional. Nunca deixa B negativo.

## Invariantes

Verificadas pelo banco, a cada transação:

- Σ D = Σ C por moeda em cada `LedgerTransaction` (constraint trigger `DEFERRABLE INITIALLY DEFERRED`).
- `amount_minor > 0` em todo lançamento.
- Conta de cliente nunca negativa (`CHECK` em `account_balances`).
- No máximo uma reserva e uma conclusão por operação externa (UNIQUE em `external_id`).
- No máximo um estorno por transação (UNIQUE em `reverses_transaction_id`).

Verificadas pela reconciliação, periodicamente:

- `account_balances.balance_minor` = saldo calculado pelos lançamentos = `balance_after_minor` do último lançamento.
- `account_sequence` sem buracos em cada conta de cliente.
- Soma geral dos lançamentos por moeda: Σ D = Σ C (balancete).
- Saldo de Clearing = soma das operações externas em `CREATED`, `UNKNOWN` e `PROCESSING`, com alerta para operações abertas há mais de N minutos.
- Movimento de Settlement = extrato do mock provider.

Qualquer divergência incrementa `ledger.balance_mismatch` e gera log com as contas envolvidas.

## Exemplo de fechamento: cenário principal da spec (§27)

Depósito de R$ 1.000 em A, depois 100 transferências internas simultâneas de R$ 100 de A para B.

| Conta | Débitos | Créditos | Saldo |
|---|---|---|---|
| 1.1.01 Funding simulado | 1.000 | 0 | 1.000 devedor |
| 2.1.0001 Cliente A | 1.000 | 1.000 | 0 |
| 2.1.0002 Cliente B | 0 | 1.000 | 1.000 credor |
| **Total** | **2.000** | **2.000** | |

10 operações `COMPLETED`, 90 `REJECTED` com `INSUFFICIENT_FUNDS` e 11 transações no ledger (1 depósito e 10 transferências).
