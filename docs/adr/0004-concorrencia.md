# ADR-004: Controle de concorrência

- Status: aceita (números de desempenho pendentes, medidos no M4). O mecanismo de lock e a atualização de saldo foram refinados pela [ADR-008](0008-imutabilidade-no-banco.md).
- Data: 2026-09-24

## Contexto

Com saldo de R$ 1.000 e 100 transferências simultâneas de R$ 100, exatamente 10 podem ser concluídas. Nenhuma conta de cliente pode ficar negativa, e A→B concorrendo com B→A não pode travar.

## Decisão

Lock pessimista na linha de `account_balances`, em isolamento `READ COMMITTED`.

```sql
SELECT ledger_account_id, balance_minor, last_sequence
FROM ledger.account_balances
WHERE ledger_account_id = ANY(@accountIds)
ORDER BY ledger_account_id
FOR UPDATE;
```

Sequência dentro da transação:

1. Reservar a chave de idempotência ([ADR-005](0005-idempotencia.md)).
2. Travar as linhas de saldo das contas de cliente envolvidas, sempre em ordem crescente de id. A ordem fixa elimina o deadlock entre A→B e B→A.
3. Com o lock na mão, ler status da conta e do cliente (Blocked, Closed) e o saldo. Ler antes do lock abriria uma janela entre a verificação e o uso.
4. Aplicar as regras de domínio: saldo suficiente e limites.
5. Inserir a transação e os lançamentos, atualizar `balance_minor` e `last_sequence`.
6. Gravar outbox e resposta de idempotência, e fazer commit.

A ordem de aquisição é sempre idempotência antes de saldos, e saldos em ordem de id. Com uma ordem global única, não há ciclo de espera.

Proteções adicionais:

- `CHECK (allow_negative OR balance_minor >= 0)` é a última barreira se o domínio falhar.
- Contas de sistema não são travadas ([ADR-003](0003-modelo-contabil.md)).
- `SET LOCAL lock_timeout = '3s'` e `statement_timeout` por transação. Lock timeout (`55P03`) e deadlock (`40P01`) viram 503 com `Retry-After`. Como a transação sofreu rollback, o cliente pode repetir com a mesma chave de idempotência.
- Sem retry automático dentro da API na Fase 1. O retry é do cliente, e a idempotência garante que é seguro.
- EF Core não gera `FOR UPDATE`. O caminho crítico usa `FromSqlInterpolated` (parametrizado) ou Dapper.
- O pool do Npgsql (padrão de 100 conexões) é dimensionado junto com o teste de carga. Cada requisição esperando o lock segura uma conexão.

## Alternativas consideradas

- **`SERIALIZABLE`.** É correto, mas numa conta disputada gera `40001` em massa e obriga a ter um laço de retry em toda operação.
- **Concorrência otimista** (`xmin` ou coluna de versão). Com 100 requisições na mesma conta, a maioria falha e tenta de novo, e a disputa vira livelock.
- **Lock distribuído no Redis.** Redundante com o lock de linha e, sem fencing token, nem é correto: um processo pausado pode continuar achando que tem o lock depois de ele expirar. Descartado, junto com o Redis na Fase 1.

## Consequências

- Operações na mesma conta são serializadas. É o comportamento desejado para uma conta, e o custo é a latência sob disputa. O teste de carga do M4 mede isso e completa esta ADR.
- Operações em contas diferentes não disputam lock entre si.
- Testes obrigatórios no M4:
  - 2 × R$ 80 com saldo de R$ 100 → 1 sucesso e 1 `INSUFFICIENT_FUNDS`;
  - 100 × R$ 100 com saldo de R$ 1.000 → 10 sucessos, saldo 0;
  - A→B e B→A em laço concorrente → nenhum deadlock;
  - rodar a suíte de concorrência várias vezes no CI para expor instabilidade.
