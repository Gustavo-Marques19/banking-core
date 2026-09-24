# Máquinas de estado

Estados das operações da Fase 1. O ledger não tem estado: uma transação contábil existe ou não existe ([ADR-003](adr/0003-modelo-contabil.md)).

Toda mudança de estado usa coluna de versão. Se duas partes tentam mudar a mesma operação, uma ganha e a outra relê. Se o estado relido já for terminal, a segunda não faz nada.

## Depósito

```mermaid
stateDiagram-v2
    [*] --> COMPLETED: operador autorizado, dentro do limite
    [*] --> REJECTED: conta bloqueada ou encerrada, limite excedido
    COMPLETED --> [*]
    REJECTED --> [*]
```

A decisão acontece numa única transação, e nenhum estado intermediário é persistido.

## Transferência interna

```mermaid
stateDiagram-v2
    [*] --> COMPLETED: saldo, status e limites ok
    [*] --> REJECTED: saldo insuficiente, conta bloqueada, limite
    COMPLETED --> [*]
    REJECTED --> [*]
```

Também é uma transação só. O estorno (M8) é uma operação nova que aponta para esta, e não muda o estado dela.

## Transferência externa

```mermaid
stateDiagram-v2
    [*] --> CREATED: Tx1, reserva em Clearing
    CREATED --> CANCELLED: cancelamento antes do envio
    CREATED --> UNKNOWN: worker grava antes de chamar o provider
    UNKNOWN --> PROCESSING: provider aceitou, ou consulta diz Processing
    UNKNOWN --> FAILED: recusa definitiva, ou consulta diz Failed
    UNKNOWN --> COMPLETED: consulta diz Completed
    UNKNOWN --> UNKNOWN: timeout, 5xx, NotFound com reenvio
    PROCESSING --> COMPLETED: webhook + consulta, ou reconciliação
    PROCESSING --> FAILED: webhook + consulta, ou reconciliação
    COMPLETED --> [*]
    FAILED --> [*]
    CANCELLED --> [*]
```

| Transição | Quem dispara | Lançamento |
|---|---|---|
| → CREATED | API (Tx1) | `D Cliente / C Clearing` |
| CREATED → CANCELLED | cliente ou operador | estorno `D Clearing / C Cliente` |
| CREATED → UNKNOWN | worker de envio, antes da chamada | nenhum |
| UNKNOWN → PROCESSING | worker ou reconciliação | nenhum |
| UNKNOWN ou PROCESSING → COMPLETED | reconciliação ou webhook (confirmado por consulta) | `D Clearing / C Settlement` |
| UNKNOWN ou PROCESSING → FAILED | worker (recusa definitiva), reconciliação ou webhook | estorno `D Clearing / C Cliente` |

`UNKNOWN` quer dizer que a ordem pode ter chegado ao provider. Por isso o worker grava esse estado antes da chamada: se o processo cair no meio, o banco já registra a dúvida.

Sem transição automática de `UNKNOWN` para `FAILED` por tempo. Depois de N tentativas sem resposta conclusiva, a operação continua em `UNKNOWN`, gera alerta e aguarda resolução manual (M8).

## Mensagem de outbox

```mermaid
stateDiagram-v2
    [*] --> PENDING: gravada na transação da operação
    PENDING --> PUBLISHED: broker confirmou (publisher confirm)
    PENDING --> PENDING: falha, RetryCount + 1, NextAttemptAt com backoff
    PENDING --> DEAD_LETTERED: tentativas esgotadas
    PUBLISHED --> [*]
    DEAD_LETTERED --> [*]
```

A entrega é pelo menos uma vez. O consumidor deduplica pelo `event_id` na tabela de inbox (ADR-007, M5).
