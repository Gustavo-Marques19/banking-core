# Saída de `make demo` e `make bench`

Execução no CI (job `demo`, runner do GitHub Actions com 4 CPUs e Ubuntu 24.04). O mesmo job roda em todo PR, e a saída completa fica no resumo da execução e no artefato `demo-output`.

```text
Banking Core: demo dos cenários da spec
Subindo Postgres e RabbitMQ (Testcontainers) e a API em processo...

== 1. Cenário principal (spec §27): saldo R$ 1.000, 100 transferências simultâneas de R$ 100 ==
   concluídas: 10, recusadas por saldo: 90
   saldo final da origem: 0.00, do destino: 1000.00
   OK      exatamente 10 concluídas
   OK      90 recusadas com insufficient_funds
   OK      saldo final R$ 0 na origem, sem saldo negativo
   OK      balancete: débitos = créditos

== 2. Mesma Idempotency-Key em 100 requisições simultâneas ==
   OK      1 operação financeira
   OK      100 respostas idênticas
   OK      debitado uma vez só

== 3. Double spending (spec §13): saldo R$ 100, duas transferências de R$ 80 ==
   OK      uma concluída
   OK      outra recusada por saldo
   OK      saldo R$ 20, nunca negativo

== 4. Broker fora do ar (spec §25.4) ==
   RabbitMQ pausado
   OK      transferência concluída mesmo sem broker
   RabbitMQ de volta
   OK      evento publicado e notificação entregue depois da volta

== 5. Transferência externa e falhas do provider (spec §18 e §19) ==
   OK      sucesso: liquida de Clearing para Settlement
   OK      recusa do provider: estorno ligado à reserva
   OK      timeout, mas o provider processou: conclui sem débito duplo
   OK      timeout sem processamento: continua UNKNOWN e vai para revisão manual
   OK      saldo R$ 700: nenhum débito duplo, nenhum estorno indevido

== 6. Reconciliação: balancete, projeção de saldo, Clearing e extrato do provider ==
   OK      nenhuma divergência

== 7. Trilha de auditoria adulterada por superusuário ==
   OK      cadeia íntegra antes da adulteração
   superusuário desligou os triggers e trocou o autor do registro 5
   OK      adulteração detectada na posição 5

Todas as verificações passaram.
```

```text
Banking Core: benchmark de transferências internas

== Ambiente: 4 CPUs, Ubuntu 24.04.5 LTS ==

== Uma conta de origem disputada (lock serializa): 300 transferências, 50 simultâneas ==
   p50 448.9 ms | p95 508.5 ms | p99 538.4 ms | máx 638.5 ms
   vazão 107 transferências/s
   OK      todas concluídas

== 50 pares de contas independentes: 1000 transferências, 50 simultâneas ==
   p50 295.7 ms | p95 351.3 ms | p99 426.6 ms | máx 486.8 ms
   vazão 166 transferências/s
   OK      todas concluídas

Todas as verificações passaram.
```

Algumas linhas informativas (contagens intermediárias, tentativas de publicação) variam de uma execução para outra e foram resumidas aqui. As verificações marcadas com OK são as mesmas em toda execução.
