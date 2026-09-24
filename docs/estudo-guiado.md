# Roteiro de estudo guiado

Para entender o projeto a ponto de defender cada decisão numa entrevista. São seis blocos, de uma a duas horas cada, na ordem em que uma requisição atravessa o sistema.

Cada bloco tem quatro partes:

1. **Ler:** arquivos e linhas, nesta ordem. Leia o ADR antes do código: o ADR diz o porquê, o código diz o como.
2. **Rodar:** os testes que provam o que você acabou de ler. Precisam de Docker, então rode no Codespaces.
3. **Prever e conferir:** um exercício em que você escreve o que acha que vai acontecer antes de rodar. Errar a previsão é onde mais se aprende.
4. **Perguntas de entrevista:** tente responder em voz alta antes de abrir a resposta.

Para rodar só uma classe de testes:

```sh
dotnet test --project tests/Banking.IntegrationTests -- --filter-class "Banking.IntegrationTests.Api.ConcurrencyTests"
```

---

## Bloco 0: o mapa (30 min)

**Ler**
- [README](../README.md), inteiro. A tabela "O que este projeto prova" é o índice do resto.
- [ADR-001](adr/0001-monolito-modular.md): por que monólito modular, um schema por módulo.
- [docs/estados.md](estados.md): as máquinas de estado. Volte aqui no bloco 4.

**Rodar**
- `make demo` no Codespaces. Leia a saída junto com [docs/demo-output.md](demo-output.md).

**Pergunta**

<details>
<summary>Por que não microserviços?</summary>

Porque as garantias que o projeto quer provar (saldo nunca negativo, operação nunca duplicada) dependem de uma transação de banco que cobre saldo, idempotência, outbox e auditoria juntos. Separar em serviços trocaria essa transação por uma saga distribuída sem ganho para o problema. As fronteiras entre módulos existem e são testadas (`tests/Banking.ArchitectureTests`), então dá para separar depois, se houver motivo.
</details>

---

## Bloco 1: dinheiro e ledger

**Ler**
1. [ADR-002](adr/0002-representacao-monetaria.md): dinheiro em centavos (`bigint`) no banco e string decimal na API.
2. `src/Banking.Domain/Common/Money.cs:38`, `TryParse`: recusa escala acima de 2 casas em vez de arredondar.
3. [ADR-003](adr/0003-modelo-contabil.md) e [docs/ledger/lancamentos.md](ledger/lancamentos.md): partida dobrada, contas de sistema Funding, Clearing e Settlement.
4. [ADR-008](adr/0008-imutabilidade-no-banco.md): o banco é a autoridade do saldo, não a aplicação.
5. `src/Banking.Infrastructure/Persistence/Migrations/LedgerDatabaseObjects.cs`:
   - linha 25, `apply_entry`: o trigger que calcula saldo e sequência em cada lançamento, com a linha de saldo travada;
   - linhas 98 a 102: os constraint triggers `DEFERRABLE INITIALLY DEFERRED` que conferem débito igual a crédito no commit;
   - linhas 112 a 122: append-only, sem UPDATE, DELETE nem TRUNCATE;
   - linha 170, `lock_balances`: `SECURITY DEFINER`, para a aplicação travar saldos sem ter UPDATE na tabela.

**Rodar**
- `Banking.IntegrationTests.Ledger.LedgerDatabaseTests`. Leia os nomes dos testes: cada um é uma regra.

**Prever e conferir**
- `Sequencia_e_saldo_enviados_pela_aplicacao_sao_ignorados` manda um INSERT com saldo e sequência inventados. Antes de ler o teste, escreva: o que o banco grava? Depois confira em `apply_entry`.

**Perguntas**

<details>
<summary>Por que calcular o saldo num trigger, e não na aplicação?</summary>

Porque qualquer caminho de escrita passa pelo trigger: a aplicação, um script, um SQL escrito à mão com a credencial da aplicação. Se o saldo fosse calculado em C#, um bug ou um acesso direto ao banco poderia gravar um saldo que não bate com os lançamentos. Assim, o saldo é sempre consequência dos lançamentos. O custo é lógica no banco, que é mais difícil de testar; por isso há uma classe de testes só para ele.
</details>

<details>
<summary>Por que a conferência débito = crédito é DEFERRED?</summary>

Os lançamentos de uma transação entram um por um. Depois do primeiro INSERT, a transação está desbalanceada por definição. A checagem precisa acontecer quando todos já entraram, e o ponto natural é o commit.
</details>

<details>
<summary>Por que dinheiro como string na API, e não número?</summary>

JSON não tem decimal. Muitos clientes leem número JSON como double, e 0.1 + 0.2 não dá 0.3 em double. String decimal chega intacta, e o parse do servidor recusa escala a mais em vez de arredondar em silêncio. No front, o valor também nunca vira `number` (`web/shared/src/money.ts`).
</details>

---

## Bloco 2: concorrência e idempotência

**Ler**
1. [ADR-004](adr/0004-concorrencia.md), com as medições no fim, e [ADR-005](adr/0005-idempotencia.md).
2. `src/Banking.Application/Transfers/TransferHandlers.cs`, de 60 a 170: a transferência interna do começo ao fim. Repare na ordem:
   - validação barata antes de abrir transação;
   - `IdempotencyRequest.Create` com o hash do pedido (linha 82);
   - `LockBalancesAsync` (linha 107) e só depois `RefreshAsync`, para decidir com o status atual da conta;
   - `InternalTransfer.Decide`, a regra de negócio pura, em `src/Banking.Domain/Payments/InternalTransfer.cs:54`;
   - outbox e auditoria na mesma transação.
3. `src/Banking.Application/Idempotency/IdempotentExecutor.cs:17`: onde a transação abre e fecha. Repare no que acontece quando `result.IsSuccess` é falso.
4. `src/Banking.Infrastructure/Platform/IdempotencyStore.cs:18`: `INSERT ... ON CONFLICT DO NOTHING`.
5. `src/Banking.Infrastructure/Persistence/EfUnitOfWork.cs:19`: `lock_timeout` e `statement_timeout` por transação.

**Rodar**
- `Banking.IntegrationTests.Api.ConcurrencyTests`: `Cem_transferencias_de_100_com_saldo_1000_exatamente_dez_passam`, `Cem_requisicoes_com_a_mesma_chave_geram_uma_operacao`, `Transferencias_cruzadas_nao_travam`.

**Prever e conferir**
- Mande duas vezes a mesma transferência, com a mesma `Idempotency-Key` e o mesmo corpo. Depois, a mesma chave com outro valor. Escreva o status e o corpo que você espera em cada caso, e confira. (Dica: procure `Idempotent-Replayed` em `src/Banking.Api/Http/ApiResults.cs`.)

**Perguntas**

<details>
<summary>100 requisições chegam juntas com a mesma chave. Como sai uma operação só?</summary>

Todas tentam `INSERT ... ON CONFLICT DO NOTHING` na tabela de chaves, dentro da própria transação. A primeira insere. As outras batem no índice único e esperam a primeira terminar, porque a linha dela ainda não foi confirmada. Quando a primeira faz commit, as outras seguem, não inserem nada, leem o resultado gravado e devolvem a mesma resposta. Se a primeira fizer rollback, uma das outras assume. Não há lock distribuído nem cache: é a garantia do índice único do Postgres.
</details>

<details>
<summary>Uma transferência recusada por saldo insuficiente é repetida com a mesma chave. O que volta?</summary>

A mesma recusa, com o mesmo id. Recusa de negócio é um resultado válido: é gravada, auditada e fica na idempotência. Já um erro de validação (valor mal formatado, conta inexistente) não passa do `if (!result.IsSuccess)` no executor: a transação é desfeita, a chave não fica reservada e o cliente pode corrigir e tentar de novo.
</details>

<details>
<summary>Por que lock pessimista e não SERIALIZABLE ou concorrência otimista?</summary>

Numa conta disputada, as duas alternativas geram falha em massa: `40001` no SERIALIZABLE, conflito de versão no otimista. Isso obriga a ter retry em toda operação, e sob muita disputa vira livelock. O lock de linha serializa as operações da mesma conta, que é o comportamento correto para uma conta. Contas diferentes não disputam entre si. O custo foi medido: cerca de 9 ms por transferência na conta disputada (ADR-004).
</details>

<details>
<summary>A→B e B→A ao mesmo tempo. Por que não dá deadlock?</summary>

`lock_balances` trava as linhas sempre em ordem crescente de id (`ORDER BY ledger_account_id ... FOR UPDATE`). Com uma ordem global única, ninguém segura um lock esperando outro que está com quem espera por ele. E se algo travar mesmo assim, `lock_timeout` corta em 3 s e a API responde 503 com `Retry-After`; como houve rollback, repetir com a mesma chave é seguro.
</details>

---

## Bloco 3: eventos (outbox e inbox)

**Ler**
1. [ADR-007](adr/0007-outbox-e-inbox.md).
2. `outbox.Enqueue` em `TransferHandlers.cs:134`: o evento é gravado na mesma transação da transferência.
3. `src/Banking.Infrastructure/Messaging/OutboxPublisher.cs`:
   - linha 114, `FOR UPDATE SKIP LOCKED`: várias instâncias dividem o trabalho sem publicar a mesma mensagem;
   - linhas 138 a 145: quando uma mensagem vai para a dead-letter, e quando não vai.
4. `src/Banking.Infrastructure/Messaging/RabbitMqPublisher.cs`: publisher confirm e `mandatory`.
5. `src/Banking.Infrastructure/Platform/InboxStore.cs:8`: a deduplicação no consumidor.

**Rodar**
- `Banking.IntegrationTests.Api.MessagingTests`, os cinco testes. `Broker_fora_do_ar_nao_para_a_operacao_e_o_evento_sai_quando_ele_volta` pausa o RabbitMQ de verdade.

**Prever e conferir**
- O broker cai por uma hora. A mensagem vai para a dead-letter? Escreva sua resposta e confira nas linhas 138 a 145 do `OutboxPublisher`.

**Perguntas**

<details>
<summary>Por que não publicar no RabbitMQ direto do handler?</summary>

Porque banco e broker não compartilham transação. Se o commit passa e a publicação falha, o evento some. Se a publicação passa e o commit falha, sai um evento de algo que não aconteceu. Com outbox, o evento vira uma linha na mesma transação da operação: ou os dois existem, ou nenhum. Um worker publica depois.
</details>

<details>
<summary>O outbox garante entrega exatamente uma vez?</summary>

Não, garante pelo menos uma vez. Se o worker publica e cai antes de marcar a mensagem como publicada, ela sai de novo. Quem transforma isso em efeito único é o consumidor: o inbox grava o id do evento com chave única, na mesma transação do efeito, e ignora repetidos. Exatamente uma vez de ponta a ponta é pelo menos uma vez com consumidor idempotente.
</details>

<details>
<summary>Por que broker fora do ar não manda a mensagem para a dead-letter?</summary>

Porque a mensagem não tem defeito, o problema é a infraestrutura. Mandar para a dead-letter transformaria uma queda passageira em evento perdido. Só vai para a dead-letter o que o broker recebeu e recusou (sem rota, `nack`), depois de várias tentativas. A queda só atrasa.
</details>

---

## Bloco 4: transferência para outro banco

**Ler**
1. [ADR-006](adr/0006-transferencia-interna-e-externa.md) e o diagrama de transferência externa em [docs/estados.md](estados.md).
2. `src/Banking.Application/ExternalTransfers/ExternalTransferProcessor.cs`:
   - linha 122, `MarkSubmitting`: grava UNKNOWN e faz commit **antes** de chamar o provider;
   - linha 136, `SubmitAsync`: a chamada, fora da transação;
   - linha 112: depois de N tentativas sem resposta, revisão manual, não FAILED.
3. `src/Banking.Domain/Payments/ExternalTransfer.cs:159`, `ApplySubmitResult`: o que cada resposta do provider faz com o estado.
4. `src/Banking.Infrastructure/Provider/MockBankingProvider.cs`: os cenários pelo prefixo da conta (`FAIL-`, `TIMEOUT-`, `LATE-`...).
5. `src/Banking.Infrastructure/Reconciliation/ReconciliationService.cs:109`: Clearing tem que bater com as transferências em aberto.

**Rodar**
- `Banking.IntegrationTests.Api.ExternalTransferTests`, um teste por cenário do mock. Comece por `Timeout_com_processamento_no_provider_conclui_sem_debito_duplo`.

**Prever e conferir**
- Cenário `LATE`: o provider conclui a transferência, mas a resposta nunca chega. Escreva por quais estados ela passa, e se o cliente é debitado uma ou duas vezes. Confira no teste acima.

**Perguntas**

<details>
<summary>Por que gravar UNKNOWN antes de chamar o provider?</summary>

Porque o processo pode cair durante a chamada. Se o estado ainda fosse CREATED, ao voltar o sistema acharia que nada foi enviado e mandaria de novo: débito duplo, se o provider já tivesse processado. Com UNKNOWN gravado antes, o banco registra a dúvida, e a saída é consultar o provider, não reenviar às cegas.
</details>

<details>
<summary>Por que timeout não vira FAILED?</summary>

Timeout quer dizer "não sei", não "falhou". Se o provider processou e o sistema marca FAILED, o valor é devolvido ao cliente e o dinheiro sai duas vezes: uma no outro banco, outra de volta na conta. Só a consulta ao provider tira do UNKNOWN. Sem resposta conclusiva depois de várias tentativas, a transferência vai para revisão manual, onde um operador registra o desfecho com evidência e outro aprova.
</details>

<details>
<summary>Para que serve a conta Clearing?</summary>

Para o dinheiro de uma transferência externa ficar em algum lugar entre sair do cliente e ser liquidado. A reserva lança `D Cliente / C Clearing`; a liquidação, `D Clearing / C Settlement`; a falha, o estorno para o cliente. Assim o saldo de Clearing tem que ser exatamente a soma das transferências em aberto, e a reconciliação confere isso. Se não bater, há dinheiro perdido ou criado em algum ponto.
</details>

---

## Bloco 5: segurança

**Ler**
1. [docs/threat-model.md](threat-model.md): 24 ameaças, cada uma com o teste que prova a mitigação. Escolha cinco e abra os testes.
2. [ADR-009](adr/0009-autenticacao-e-autorizacao.md):
   - `src/Banking.Api/Composition/AuthenticationSetup.cs:34`: só RS256;
   - `src/Banking.Application/Accounts/AccountHandlers.cs:33`, `AccountAccess`: conta de outra pessoa responde 404, não 403.
3. `src/Banking.Infrastructure/Security/AesGcmDocumentProtector.cs:37`: AES-GCM com o id do cliente como dado associado, mais um HMAC separado para buscar por CPF sem descriptografar.
4. `src/Banking.Infrastructure/Persistence/Migrations/AuditDatabaseObjects.cs:34` e `src/Banking.Infrastructure/Audit/AuditTrail.cs:89`: a cadeia de hash, calculada no banco e verificada fora dele.
5. [ADR-011](adr/0011-bff-para-os-fronts.md): o BFF.
   - `src/Banking.Bff/BffProgram.cs:114`: o proxy tira o cookie e põe o token;
   - `src/Banking.Bff/Security.cs:7`: CSRF por cabeçalho;
   - `src/Banking.Bff/BffProgram.cs:79`: o endereço público atrás de proxy (Codespaces).
6. `web/customer/src/views/Transfer.tsx`: a chave de idempotência nasce na tela de resumo (linhas 84 e 99) e é repetida em todo reenvio (linha 265).

**Rodar**
- `AuditTests.Alteracao_feita_por_superusuario_e_detectada` e `AuditTests.Registro_apagado_por_superusuario_e_detectado`.
- `HardeningTests.Deposito_acima_do_limite_de_aprovacao_espera_outro_operador`.
- `Banking.IntegrationTests.Api.BffTests`, com login real no Keycloak.

**Prever e conferir**
- No terminal do Codespaces, pegue um token da `alice` e peça o extrato de uma conta do `bruno` (o id sai de `GET /api/v1/accounts` com o token dele). Escreva o status antes de rodar.

  ```sh
  token() { curl -fsS -d grant_type=password -d client_id=banking-cli -d "username=$1" -d "password=$1-dev-only" \
    http://keycloak:8080/realms/banking/protocol/openid-connect/token | sed -E 's/.*"access_token":"([^"]+)".*/\1/'; }
  curl -s -w "\n%{http_code}\n" -H "Authorization: Bearer $(token alice)" localhost:5080/api/v1/accounts/<id-da-conta-do-bruno>/transactions
  ```

**Perguntas**

<details>
<summary>Por que 404 e não 403 para a conta de outra pessoa?</summary>

403 confirma que o id existe, e isso já vaza informação: dá para testar ids e descobrir quais são contas reais. 404 responde igual para "não existe" e para "existe, mas não é sua".
</details>

<details>
<summary>A auditoria tem hash encadeado. Um superusuário do banco consegue adulterar?</summary>

Consegue alterar, não consegue esconder. Cada registro guarda o hash do anterior, e a verificação recalcula a cadeia fora do banco, em C#. Mudar um registro quebra o hash dele e de todos os seguintes. O ponto fraco, documentado: apagar o último registro não quebra nada. A defesa seria publicar o hash mais recente fora do banco de tempos em tempos.
</details>

<details>
<summary>Por que um BFF, e não o token no navegador?</summary>

Token em localStorage ou na memória do JavaScript fica ao alcance de qualquer XSS, e um token roubado funciona de qualquer lugar até expirar. Com o BFF, o navegador só tem um cookie `HttpOnly` que o JavaScript não lê; o token fica no servidor. O CSRF, que é o risco de usar cookie, fica fechado por `SameSite=Strict` e pelo cabeçalho `X-CSRF`, que outro site não consegue mandar sem CORS.
</details>

<details>
<summary>Por que CPF com AES-GCM e mais um HMAC?</summary>

AES-GCM protege o valor e detecta adulteração; com o id do cliente como dado associado, um CPF copiado para outro cliente não descriptografa. Mas o resultado muda a cada gravação (nonce aleatório), então não dá para procurar por ele. O HMAC é um índice cego: determinístico, com chave própria, serve para achar CPF duplicado sem guardar o CPF em claro.
</details>

---

## Bloco 6: operação

**Ler**
- A seção "Investigando uma transferência" do [README](../README.md).
- [ADR-010](adr/0010-ambiente-de-desenvolvimento.md): o ambiente de desenvolvimento.

**Fazer**
1. No Codespaces, `./scripts/dev.sh` e `./scripts/deposit.sh carla 1000`.
2. Faça uma transferência pelo app e copie o código do comprovante.
3. No backoffice, cole o código em Auditoria. Copie o trace id.
4. Procure o trace id no Aspire Dashboard (porta 18888) e siga a requisição até o banco e a mensagem publicada.

<details>
<summary>Um cliente diz que foi debitado duas vezes. Por onde você começa?</summary>

Pelo código da operação no comprovante ou no extrato. Com ele: a trilha de auditoria (quem pediu, resultado, trace id), os lançamentos no ledger (`GET /api/v1/ledger/transactions/{id}`) e a resposta de idempotência. Se forem duas operações com chaves diferentes, foram dois pedidos: a pergunta passa a ser por que o cliente mandou duas vezes. Se for uma operação com dois débitos, a reconciliação já teria apontado, porque o ledger não fecharia.
</details>

---

## Fraquezas que você deve saber admitir

Entrevistador experiente pergunta o que está ruim. Saber responder vale mais que fingir que está tudo certo.

- **A auditoria é serializada globalmente** por advisory lock, para a cadeia de hash ter ordem única. É o gargalo provável de vazão, suspeito e ainda não medido isoladamente (ADR-004).
- **Apagar o último registro da auditoria não é detectado** pela verificação sozinha.
- **Os tipos do front são escritos à mão**, porque a API não publica o formato das respostas no OpenAPI. Uma mudança na API pode quebrar o front sem aviso de compilação.
- **A reconciliação varre tudo** a cada execução.
- **Keycloak em modo de desenvolvimento** e chaves do Data Protection efêmeras no BFF: não é configuração de produção.
- **O provider é um mock em processo.** O contrato é assíncrono como os BaaS reais, mas nada foi testado contra um provider de verdade.

## Autoavaliação

Você terminou o roteiro quando consegue, sem olhar:

- [ ] desenhar no papel os lançamentos de uma transferência externa que falha;
- [ ] explicar por que 100 requisições com a mesma chave geram uma operação;
- [ ] dizer o que acontece se o processo cair entre gravar UNKNOWN e chamar o provider;
- [ ] explicar por que o evento nunca se perde e por que pode chegar duas vezes;
- [ ] listar três ameaças do threat model e o teste que prova cada mitigação;
- [ ] citar duas fraquezas do projeto e como você as resolveria.
