# ADR-005: Idempotência

- Status: aceita
- Data: 2026-09-24

## Contexto

A spec pede `UNIQUE(Key)` global e sugere Redis como cache de idempotência. Os dois causam defeitos:

- Com chave global, um usuário que reenvia a chave de outro recebe a resposta dele, com dados de conta alheia.
- Se a idempotência mora no Redis e o commit no Postgres dá certo mas a escrita no Redis falha, a repetição executa a operação de novo.
- A spec não diz o que acontece com duas requisições simultâneas com a mesma chave, nem quais respostas são guardadas.

## Decisão

### Onde e com qual escopo

- Tabela `platform.idempotency_keys` no Postgres, gravada **na mesma transação** da operação.
- Chave primária `(client_id, operation, key)`:
  - `client_id` é o `sub` do token;
  - `operation` é o nome da operação (`transfers.create`, `deposits.create`);
  - `key` é o header `Idempotency-Key`, de 1 a 64 caracteres em `[A-Za-z0-9_-]` (recomendado: UUID ou ULID).
- Colunas: `request_hash`, `response_status`, `response_body` (jsonb), `resource_id`, `created_at`, `expires_at` (24 h).
- O header é obrigatório em `POST /transfers` e `POST /accounts/{id}/deposits`. Sem ele, a resposta é 400.

### Fluxo

1. Validar o formato da requisição. Erro de validação devolve 400 e **não** consome a chave.
2. `BEGIN`.
3. `INSERT ... ON CONFLICT DO NOTHING RETURNING`.
   - Inseriu: seguir para a operação.
   - Conflito: a linha já existe e está commitada. Se outra requisição com a mesma chave estiver em andamento, o `INSERT` espera ela terminar, então nunca se vê um estado "em processamento".
     - Hash igual: devolver a resposta gravada, com o header `Idempotent-Replayed: true`.
     - Hash diferente: 409 `idempotency_key_reused`.
4. Executar a operação.
5. Gravar a resposta na linha reservada.
6. `COMMIT`.

### O que é guardado

| Resultado | Guardado? | Motivo |
|---|---|---|
| 2xx | Sim | Operação concluída. |
| 422 de regra de negócio (`INSUFFICIENT_FUNDS`, conta bloqueada, limite) | Sim | A operação foi avaliada. Repetir tem que dar a mesma resposta. |
| 400 de validação | Não | A operação nem começou. O cliente corrige e reenvia com a mesma chave. |
| 5xx, lock timeout, deadlock | Não | Houve rollback, nada foi gravado. Repetir com a mesma chave é seguro. |

### Hash da requisição

SHA-256 sobre operação, parâmetros de rota e o DTO já normalizado (valor convertido para centavos, moeda em maiúsculas), serializado com ordem de propriedades fixa. `"100.0"` e `"100.00"` produzem o mesmo hash. Bytes crus do body não são usados, porque espaço e ordem de campos mudariam o hash.

### Segunda linha de defesa

- A tabela da operação guarda a chave com `UNIQUE(requested_by, idempotency_key)`, sem expiração.
- `ledger_transactions.external_id` é UNIQUE e derivado do id da operação.
- Uma chave expirada e reenviada bate no UNIQUE da operação e recebe 409, em vez de executar de novo.

## Alternativas consideradas

- **Redis como store principal.** Duplica operações quando o commit e a escrita no Redis divergem. Descartado.
- **Gravar a chave só no final, junto com a resposta.** Também é correto (a segunda requisição falha no UNIQUE e faz rollback), mas as duas executam a lógica inteira e disputam os locks de saldo. Reservar no início faz a segunda esperar barato.
- **Estado explícito "em processamento" com resposta 409 imediata.** Necessário quando a operação atravessa várias transações. Aqui a operação cabe em uma só, e esperar o commit é mais simples e dá ao cliente a resposta real.

## Consequências

- 100 requisições com a mesma chave produzem 1 operação e 100 respostas iguais, sem Redis.
- A chave depende de identidade autenticada. Idempotência e autenticação ([ADR-009](README.md), M3) andam juntas.
- Um job remove linhas expiradas da tabela de idempotência. As tabelas de operação guardam a chave para sempre.
- Transferência externa: a chave cobre a criação da operação (Tx1). A chamada ao provider usa o id da operação como chave própria ([ADR-006](0006-transferencia-interna-e-externa.md)).
