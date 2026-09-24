# ADR-007: Outbox e inbox

- Status: aceita
- Data: 2026-09-24

## Contexto

Operação e evento precisam andar juntos. Publicar no broker depois do commit perde o evento se o broker falhar; publicar antes do commit anuncia uma operação que pode sofrer rollback. O broker também entrega mensagens mais de uma vez.

## Decisão

**Outbox** (`platform.outbox_messages`)
- O evento é gravado na mesma transação da operação, com envelope `{eventId, type, version, occurredAt, data}` e o `traceparent` do momento.
- Um worker publica o que está pendente, com `FOR UPDATE SKIP LOCKED`: várias instâncias dividem o trabalho sem publicar a mesma mensagem ao mesmo tempo.
- A mensagem só é marcada como publicada depois do publisher confirm do RabbitMQ.
- Publicação com `mandatory`: sem fila ligada, o broker devolve a mensagem e ela continua pendente, em vez de sumir.
- O lote para na primeira falha. Com o broker fora, insistir nas mensagens seguintes só gasta tempo e esconde a falha até o commit.
- Backoff exponencial com jitter, limitado por `Outbox:MaxRetryDelay`.
- **Queda de conexão nunca leva à dead-letter.** Só recusa da própria mensagem (sem rota, nack) conta para `Outbox:MaxAttempts`. Um broker fora por uma hora não pode descartar uma hora de eventos.
- Mensagens publicadas são apagadas depois de `Outbox:PublishedRetention` (7 dias).

**Inbox** (`platform.inbox_messages`)
- O consumidor grava `(consumer, event_id)` na mesma transação do efeito. Se a linha já existe, o evento é confirmado ao broker e ignorado.
- Mensagem ilegível vai para a fila de dead-letter do consumidor (`banking.notifications.dlq`), sem bloquear as outras.

**Ordem**
- O publisher lê por `occurred_at`, mas `SKIP LOCKED` com várias instâncias não garante ordem global. Consumidores tratam cada evento de forma independente (notificações). Um consumidor que precise de ordem por agregado usa a sequência do ledger, não a ordem de chegada.

## Alternativas consideradas

- **Publicar direto no broker após o commit.** Perde o evento na falha entre os dois passos. Descartado.
- **Change data capture (Debezium lendo o WAL).** Tira o polling e a tabela extra, mas acrescenta Kafka Connect e mais uma peça para operar. Exagero para a Fase 1.
- **Dead-letter por número de tentativas, sem distinguir a causa.** Foi a primeira implementação. O teste de broker pausado mostrou eventos indo para a dead-letter depois de um minuto de queda. Corrigido.

## Consequências

- Entrega pelo menos uma vez, de ponta a ponta. Todo consumidor novo precisa de inbox.
- Latência de publicação igual ao intervalo de polling (`Outbox:PollInterval`, 1 s por padrão).
- `outbox.pending` crescendo é o sinal de broker fora ou lento (métrica no M7).
