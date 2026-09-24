# ADR-006: Transferência interna e transferência externa

- Status: aceita
- Data: 2026-09-24

## Contexto

A spec descreve a transferência como atômica e síncrona (§11), mas também passa por um provider externo que pode dar timeout e deixar a operação em `UNKNOWN` (§17 a §19). As duas coisas não cabem no mesmo fluxo. Se as duas contas estão no nosso ledger, não há motivo para chamar o provider.

## Decisão

São duas operações diferentes, com endpoints, estados e garantias próprios.

### Transferência interna (`POST /api/v1/transfers`)

- Origem e destino são contas deste banco.
- Uma transação de banco, sem provider e sem I/O de rede.
- Resultado síncrono:
  - `201` com status `COMPLETED`;
  - `422` com status `REJECTED` e o motivo.
- A operação rejeitada também é gravada (auditoria e repetição idempotente), mas sem lançamento no ledger.
- Nunca fica em `UNKNOWN`.

### Transferência externa (`POST /api/v1/external-transfers`)

Cash-out para fora do banco, por meio do provider. É uma saga em etapas:

1. **Tx1, na API.** Idempotência, lock da conta de origem, verificação de saldo e limites, lançamento `D Cliente / C Clearing`, operação em `CREATED`, outbox. Responde `202` com o id da operação.
2. **Worker de envio.** Busca operações em `CREATED` com `FOR UPDATE SKIP LOCKED`. **Antes** de chamar o provider, grava `UNKNOWN` e faz commit. Se o processo cair durante a chamada, o estado persistido já diz a verdade: a ordem pode ter chegado ao provider.
3. **Chamada ao provider**, fora de qualquer transação, com o id da operação como `clientReference` (a chave de idempotência do lado do provider).
4. **Tx2, conforme a resposta:**

   | Resposta do provider | Novo estado | Lançamento |
   |---|---|---|
   | Aceita | `PROCESSING` | nenhum |
   | Já existe (DUPLICATE) | `PROCESSING` | nenhum; confirma por consulta de status |
   | Recusa definitiva | `FAILED` | estorno `D Clearing / C Cliente` |
   | Timeout, HTTP 5xx, erro de rede | continua `UNKNOWN` | nenhum |

5. **Conclusão**, por webhook ou consulta de status: `COMPLETED` com `D Clearing / C Settlement`, ou `FAILED` com o estorno.

A fila de trabalho é a própria tabela de operações. O worker não depende do broker, então a transferência externa funciona com o RabbitMQ fora do ar. O outbox publica eventos para outros consumidores.

### Regras para UNKNOWN

- `UNKNOWN` nunca vira `FAILED` sem confirmação do provider.
- O job de reconciliação consulta `GetTransferStatusAsync(clientReference)`:
  - `Completed` ou `Failed`: aplica a Tx2 correspondente;
  - `Processing`: vai para `PROCESSING`;
  - `NotFound`: reenvia com o mesmo `clientReference` (o provider deduplica), até N tentativas com backoff.
- Esgotadas as tentativas, a operação continua `UNKNOWN`, gera alerta e passa a exigir resolução manual por operador (M8).
- O valor fica em Clearing enquanto a operação não termina. Por isso o saldo de Clearing tem que ser igual à soma das operações em aberto.

### Proteção contra lançamento duplo

- Tx1 usa `external_id = "{operationId}:reservation"`.
- A conclusão, seja liquidação ou estorno, usa `external_id = "{operationId}:resolution"`.
- Como `external_id` é UNIQUE, o banco garante no máximo uma conclusão por operação. Webhook e reconciliação chegando juntos não conseguem liquidar duas vezes, nem liquidar e estornar a mesma operação.
- Mudanças de estado usam coluna de versão (concorrência otimista). Quem perde a corrida relê o estado e, se já for terminal, não faz nada.

### Contrato do provider

```csharp
public interface IBankingProvider
{
    Task<SubmitResult> SubmitTransferAsync(ProviderTransferRequest request, CancellationToken ct);
    Task<ProviderTransferStatus> GetTransferStatusAsync(string clientReference, CancellationToken ct);
}
```

- `SubmitResult`: `Accepted`, `AlreadyExists`, `Rejected(reason)` ou `Unknown`. A infraestrutura converte timeout, 5xx e erro de rede em `Unknown`. O domínio nunca vê exceção de HTTP.
- `ProviderTransferStatus`: `NotFound`, `Processing`, `Completed` ou `Failed(reason)`.
- Webhook do provider em `POST /webhooks/provider`:
  - assinatura HMAC-SHA256 sobre timestamp + body;
  - rejeita timestamp com mais de 5 minutos de diferença;
  - deduplica pelo id do evento (inbox).
- O webhook só **dispara** uma consulta de status. O lançamento usa a resposta da consulta, nunca o conteúdo do webhook.
- Cenários do mock (SUCCESS, FAILED, TIMEOUT, HTTP 500, DUPLICATE, UNKNOWN e sucesso tardio) são escolhidos por requisição, para que testes paralelos não interfiram entre si.

### Holds

Não entram na Fase 1. A reserva é feita por lançamento em Clearing, então o ledger continua sendo a única fonte de verdade e o saldo disponível é igual ao saldo contábil. `GET /balance` devolve `ledgerBalance` e `availableBalance` desde já, iguais na Fase 1, para o contrato não quebrar quando holds forem necessários (autorização de cartão, por exemplo).

## Alternativas consideradas

- **Chamar o provider dentro da transação.** Segura os locks de saldo durante I/O de rede e deixa o estado incoerente se o commit falhar depois de o provider aceitar. Descartado.
- **Hold em vez de Clearing.** Não gera lançamento até a confirmação, o que deixa o estorno desnecessário. Custa uma tabela a mais, a fórmula `disponível = contábil − holds` em toda verificação de saldo, e uma parte do dinheiro comprometido fica fora do ledger. Com Clearing, a reconciliação verifica tudo pelo ledger.
- **Worker disparado pelo broker.** Tornaria o RabbitMQ obrigatório para a operação funcionar. Com a tabela como fila, o broker é opcional para o fluxo financeiro.

## Consequências

- Interna e externa são endpoints diferentes, e o cliente sabe qual garantia está recebendo.
- `CANCELLED` só é possível a partir de `CREATED`, antes do envio. Depois que o worker grava `UNKNOWN`, cancelar deixa de ser permitido.
- O contrato do provider já nasce assíncrono, próximo do formato dos BaaS reais. A Fase 2 implementa `IBankingProvider` sem mudar o domínio.
- As máquinas de estado estão em [estados.md](../estados.md) e os lançamentos em [ledger/lancamentos.md](../ledger/lancamentos.md).
