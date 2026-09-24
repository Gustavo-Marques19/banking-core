# Plano revisado da Fase 1: Resilient Banking Core

Revisão crítica do documento [`fase-1-resilient-banking-core.md`](spec/fase-1-original.md), feita sob três ângulos (arquitetura, segurança e contabilidade), e o plano de execução que sai dela.

---

## Parte 1: Crítica

### Veredito

A lista de propriedades está certa: idempotência, outbox, double-entry, UNKNOWN. O documento, porém, tem três falhas estruturais:

1. **Ele contradiz a si mesmo no ponto central.** A transferência é descrita como atômica e síncrona (§11), mas também passa por um provider externo que pode dar timeout e ficar em UNKNOWN (§19, §23). As duas coisas não cabem no mesmo fluxo, e o documento não diz em que momento o provider é chamado.
2. **O objetivo declarado é segurança, e a spec não tem nada de segurança.** Não há autenticação nem autorização. Qualquer pessoa com `curl` cria dinheiro (`POST /deposits`) e transfere da conta de qualquer outra (`sourceAccountId` vem no body). "Segurança" não aparece no Definition of Done.
3. **O ledger ainda não é double-entry.** O depósito é um `CREDIT 1000` sem contrapartida, o que viola o §9 da própria spec. Faltam plano de contas, contas internas, holds e a convenção de débito e crédito.

Além disso, a infraestrutura é pedida antes de haver quem precise dela. Redis, RabbitMQ e Grafana entram antes de existir um consumidor de eventos ou um problema que o Redis resolva.

### 1.1 Contradições e lacunas de arquitetura

| Problema | Por que é grave | Correção |
|---|---|---|
| Transferência interna chamando o provider | A e B estão no **seu** ledger. Chamar provider para mover dinheiro entre contas próprias não faz sentido e cria o UNKNOWN sem necessidade. | Separar em dois fluxos. `InternalTransfer` (book transfer) é atômica, sem provider, e termina em COMPLETED ou REJECTED. `ExternalTransfer` (cash-out) é uma saga assíncrona que usa o provider. |
| Onde o provider é chamado | Chamada de rede dentro da transação segura os row locks durante o I/O. | **Nunca** chamar dentro da transação. Tx1 lança a reserva em Clearing e escreve no outbox. Um worker chama o provider. Tx2 confirma ou estorna. |
| UNKNOWN sem saída | UNKNOWN sem reconciliação é um beco sem saída. | `IBankingProvider.GetTransferStatusAsync(ourId)` e um job de reconciliação já na Fase 1. |
| Interface moldada pelo mock | BaaS reais (Celcoin etc.) são assíncronos, com webhook. `TransferAsync` retornando o resultado final quebra na Fase 2. | Contrato "submit → accepted" mais consulta de status e webhook simulado. |
| `LedgerTransaction.Status` | Status em registro imutável é contradição. | Status fica em `Transfer`/`Deposit` (a intenção). O ledger guarda só lançamentos postados. |
| Payment × Transaction × LedgerTransaction | Três conceitos sobrepostos, sem definição. | Dois conceitos: **Operação** (Transfer/Deposit, com máquina de estados) e **LedgerTransaction** (lançamento contábil imutável). |
| "Ledger é a fonte da verdade, sem campo Balance" | Não dá para fazer `FOR UPDATE` num `SUM()`, e `SUM` por transferência custa O(n). | Tabela `account_balances` atualizada **na mesma transação** dos lançamentos. É uma projeção transacional, reconciliável com o ledger. |
| Body de transfer sem `currency`, deposit sem `Idempotency-Key` | Buracos na API. | Incluir os dois. |
| `Money(decimal, string)` como `readonly record struct` | `default(Money)` passa por fora de qualquer validação (Amount 0, Currency null), e `string` aceita "brl", "XYZ" etc. | `Currency` como value object ISO 4217. No banco e no JSON, valor como `bigint` em centavos ou string decimal, nunca number float. |
| Admin `/provider/scenario` global | Quebra testes paralelos e é um DoS de uma chamada só. | Cenário **por requisição** (header ou regra por conta) nos testes. Endpoint admin só em Development e com policy `admin`. |
| DoD em forma de checkbox de tecnologia | "[x] Outbox" não prova nada. | DoD com **critérios verificáveis**, cada um apontando para um teste (ver Parte 3). |

### 1.2 Concorrência: decisão recomendada

**Lock pessimista na linha de saldo, ordenado por id.**

```sql
SELECT account_id, balance
FROM account_balances
WHERE account_id IN (@a, @b)
ORDER BY account_id          -- evita deadlock entre A→B e B→A simultâneos
FOR UPDATE;
```

- Status (Blocked/Closed) e saldo são verificados **dentro** do lock. Verificar antes abre uma janela TOCTOU.
- Um `CHECK (balance >= 0)` nas contas de cliente é a última barreira.
- A conta de funding/sistema **não** leva lock nem check de saldo. Ela vira hotspot, e contas de sistema podem ficar negativas.
- Configurar `lock_timeout` e dimensionar o pool do Npgsql (o padrão é 100). Sem isso, o teste de 100 requisições falha por timeout em vez de devolver INSUFFICIENT_FUNDS.
- EF Core não gera `FOR UPDATE`. No caminho crítico, usar `FromSql` ou Dapper.

Por que não as alternativas:
- **SERIALIZABLE:** é correto, mas numa conta disputada gera `40001` em massa e exige um loop de retry.
- **Otimista (`xmin`/version):** vira livelock com 100 requisições na mesma conta.
- **Lock distribuído no Redis:** redundante com o row lock e, sem fencing token, nem é correto. **Remover.**

Registrar essa decisão numa ADR, com os números do teste de carga.

### 1.3 Idempotência: o desenho da spec vaza e duplica

- **`UNIQUE(Key)` global vaza dados.** Se B reenvia a chave de A, recebe a resposta de A. O correto é `UNIQUE(client_id, operation, key)`, e isso exige identidade (autenticação).
- **Guardar no Postgres, na mesma transação do ledger.** Com `INSERT ... ON CONFLICT`, a segunda requisição concorrente bloqueia até a primeira fazer commit e então lê a resposta gravada. Isso resolve o estado "em andamento" e o cenário "100 requisições → 1 operação" sem Redis.
- **Redis como store de idempotência duplica operações.** Se o commit dá certo e a escrita no Redis falha, a operação é reexecutada.
- **Política explícita:** rejeição de negócio (INSUFFICIENT_FUNDS) é **gravada** e repetida. Erro 5xx faz rollback e permite retry.
- `RequestHash` calculado sobre rota + JSON canônico, não sobre os bytes crus.
- Segunda linha de defesa: `UNIQUE(external_id)` no ledger, para o caso de a chave já ter expirado.

### 1.4 Outbox: faltam metade das peças

- Entrega at-least-once exige **inbox no consumidor**: `UNIQUE(consumer, event_id)` gravado na mesma transação do efeito. Sem isso, o cenário 9 dos testes ("evento duplicado") não tem como passar.
- Com vários workers, usar `FOR UPDATE SKIP LOCKED LIMIT n`. Isso quebra a ordem global, então a ordem precisa ser garantida por agregado ou tolerada pelo consumidor (versão).
- Não fazer polling por `id > último` com bigserial: ids não aparecem na ordem de commit e mensagens se perdem. Fazer polling por status.
- Faltam `NextAttemptAt` com backoff exponencial, status `DeadLettered` e limpeza da tabela.
- **Na Fase 1 ninguém consome os eventos.** Broker sem consumidor é enfeite, então é preciso criar um consumidor real (ver M5).
- `LedgerEntryCreated` por lançamento é ruído. `TransferCreated` mais `TransferCompleted` na mesma transação atômica é redundante.

### 1.5 Ledger / contabilidade

O modelo decidido no M0 está na [ADR-003](adr/0003-modelo-contabil.md) e em [ledger/lancamentos.md](ledger/lancamentos.md). Resumo:

- Plano de contas com quatro contas: Funding simulado (1.1.01), Settlement no provider (1.1.02), uma conta de passivo por cliente (2.1.*) e Clearing de transferências externas (2.2.01). Suspense e Capital ficaram de fora até existir um lançamento que precise delas.
- `Account` (produto do cliente) é separado de `LedgerAccount` (conta contábil).
- Depósito: `D 1.1.01 Funding` / `C 2.1.* Cliente`.
- Convenção única: `direction` (D/C) mais `amount_minor > 0` com CHECK. Conta de cliente é passivo: saldo = créditos − débitos.
- `sequence` e `balance_after` existem só nas contas com saldo materializado (as de cliente). Contas de sistema não levam lock, senão viram gargalo.
- Transferência externa usa Clearing, não holds ([ADR-006](adr/0006-transferencia-interna-e-externa.md)). Enquanto a operação está em UNKNOWN, o valor fica em Clearing e só a reconciliação resolve.

**Invariantes de reconciliação** (são a fonte da métrica `ledger.balance_mismatch`, que hoje não tem ninguém para alimentá-la):

- **Síncrono**, por transação: Σ débitos = Σ créditos por moeda. Garantido por constraint trigger `DEFERRABLE INITIALLY DEFERRED` no Postgres.
- **Contínuo:** `account_balances` = Σ entries = último `balance_after`. Sequence sem buracos. Nenhum cliente com saldo devedor.
- **Diário:** trial balance (Σ geral por moeda = 0). Clearing = Σ operações em PROCESSING/UNKNOWN, com aging. Settlement × extrato do mock (o mock precisa gerar extrato).

### 1.6 Segurança: o que falta, por prioridade

**Bloqueia a Fase 1 (sem isso não é um "banco seguro"):**

1. **Autenticação.** Keycloak no docker-compose, com realm exportado e versionado. OIDC com JWT RS256, validando `iss`, `aud`, `exp` e o algoritmo. Não implementar login próprio.
2. **Autorização por recurso (BOLA/IDOR).** A conta de origem precisa pertencer ao `sub` do token, verificado **dentro** da transação. Recurso de outro dono devolve 404, não 403. O teste "A tenta mover dinheiro de B" entra no DoD.
3. **Depósito controlado.** Role `operator` com scope `deposits:write`, limite por operação e por dia, e campo de motivo.
4. **Imutabilidade garantida pelo banco, não só pelo domínio:**
   - Role `migrator` dona do schema e role `app` sem privilégios de DDL.
   - `REVOKE UPDATE, DELETE, TRUNCATE` nas tabelas de ledger e auditoria para `app`.
   - Trigger `BEFORE UPDATE OR DELETE` que lança exceção.
5. **Validação de entrada:**
   - Rejeitar (não arredondar) escala maior que a da moeda.
   - Teto de valor.
   - Rejeitar `source == destination`.
   - `UnmappedMemberHandling.Disallow` no JSON.
   - Status Blocked/Closed dos dois lados.
6. **Limites.** Por transação, diário e de velocidade, calculados no Postgres na mesma transação, não no Redis.
7. **Segredos.** Nada em `appsettings`, `.env` fora do git, e nada de `guest/guest` no RabbitMQ. Erros como ProblemDetails, sem stack trace.

**Alto (Fase 1 tardia):**

8. **Audit log à prova de adulteração.** Tabela própria com `prev_hash` e `hash` (SHA-256 sobre payload canônico), mais um job que verifica a cadeia. O `Actor` vem do token. Para portfólio, é uma demo excelente: altere uma linha como superuser e mostre a verificação acusando.
9. **CPF / LGPD:**
   - Validar os dígitos verificadores.
   - Criptografar a coluna (AES-GCM ou Data Protection), com blind index HMAC para busca e unicidade.
   - Mascarar nos logs.
   - **Nada de CPF em eventos/outbox.**
   - Seed só com CPFs gerados.
10. **Maker-checker** para estorno, depósito acima do limite e bloqueio de conta, com aprovador diferente do solicitante (regra de domínio).
11. **Rate limiting** nativo do ASP.NET, particionado por `sub`.
12. **Threat model STRIDE** em `docs/threat-model.md`, com cada ameaça ligada a um teste.

**CI que vale a pena** (barato e visível): gitleaks, Dependabot, CodeQL, Trivy na imagem e SBOM CycloneDX.
**Exagero agora:** Vault, mTLS num monólito, cosign/SLSA, DAST completo, HSM.

### 1.7 Escopo: cortar

| Item da spec | Decisão | Motivo |
|---|---|---|
| Redis | **Cortar** | Idempotência e lock ficam no Postgres, e rate limiting é nativo. Redis só adicionaria um modo de falha. |
| RabbitMQ | **Adiar para M5**, com consumidor real | Sem consumidor, é enfeite. |
| Prometheus/Grafana | **Trocar** pelo .NET Aspire Dashboard | OpenTelemetry pronto, zero configuração. Grafana pode vir no fim, se quiser. |
| SQL Server / Azure Service Bus | **Descartar alternativas** | Decida: Postgres + RabbitMQ. |
| React client | **Fora da Fase 1** | Swagger/Scalar e um script de demo bastam. UI é outra habilidade e dilui o foco. |
| `LedgerEntryCreated` | **Cortar** | Ruído. |

---

## Parte 2: Decisões (viram ADRs em `docs/adr/`)

1. **ADR-001: Modular monolith**, com módulos Ledger, Accounts, Payments e Identity, e fronteiras garantidas por ArchitectureTests.
2. **ADR-002: Representação monetária:** `bigint` em minor units, `Currency` ISO 4217 como VO, JSON com string decimal.
3. **ADR-003: Modelo contábil:** plano de contas, convenção D/C, `Account` ≠ `LedgerAccount`.
4. **ADR-004: Concorrência:** `FOR UPDATE` ordenado em `account_balances` (com dados do teste de carga).
5. **ADR-005: Idempotência:** Postgres, mesma transação, escopo `(client, operation, key)`.
6. **ADR-006: Transferência interna × externa:** book transfer atômica × saga com Clearing.
7. **ADR-007: Outbox/Inbox:** at-least-once, SKIP LOCKED, backoff, DLQ.
8. **ADR-008: Imutabilidade no banco:** roles, REVOKE, triggers, constraint trigger de balanceamento.
9. **ADR-009: AuthN/AuthZ:** Keycloak, OIDC, autorização por recurso.
10. **ADR-010: Ambiente de desenvolvimento:** GitHub Codespaces + devcontainer, com CI no GitHub Actions. Vercel foi descartada porque não roda .NET como runtime oficial, processos contínuos (worker do outbox), RabbitMQ, Keycloak nem Docker. A demo pública, se houver, fica para o M8 num host de containers.

---

## Parte 3: Plano de execução

Cada milestone termina com **algo demonstrável e testado**. Não avance com testes vermelhos.

### M0: Design no papel (2 a 4 dias)
- [x] ADR-001 a ADR-006 escritas, mais a ADR-010 ([índice](adr/README.md)).
- [x] Plano de contas e lançamentos de cada operação (depósito, transferência interna, externa com sucesso, falha e UNKNOWN) desenhados em tabela ([lancamentos.md](ledger/lancamentos.md)).
- [x] Máquinas de estado de depósito, transferência interna, externa e outbox ([estados.md](estados.md)).
- [x] Threat model STRIDE v0, com 18 ameaças ([threat-model.md](threat-model.md)).

**Critério:** você consegue explicar a qualquer pessoa, sem código, o que acontece no banco em cada cenário de falha.

### M1: Fundação (4 a 6 dias)

**Primeiro passo: ambiente no Codespaces (`.devcontainer/`).** Todo o resto do M1 é feito dentro dele.

- [x] `.devcontainer/devcontainer.json` usando `dockerComposeFile`, com três serviços:
  - `app`: imagem `mcr.microsoft.com/devcontainers/dotnet` com o SDK do .NET 10, onde você programa;
  - `postgres`: imagem alpine com **versão fixada** e volume nomeado para os dados;
  - `keycloak`: `start-dev --import-realm`, lendo o realm versionado em `.devcontainer/keycloak/realm-banking.json`.
- [x] Feature `docker-in-docker`, para que os Testcontainers funcionem dentro do codespace.
- [x] `postCreateCommand`: `dotnet restore` e `dotnet tool restore` (dotnet-ef fixado em `dotnet-tools.json` na raiz, o padrão do .NET 10) e migrations aplicadas.
- [x] Script de init do Postgres criando as roles `migrator` e `app`. O ambiente já nasce com a separação de privilégios.
- [x] `forwardPorts` para a API, o Keycloak e o Postgres (o Aspire Dashboard entra no M7), com `portsAttributes` definindo labels. Todas as portas ficam **privadas** (o padrão do Codespaces). Nunca torne pública uma porta do Keycloak ou da API.
- [x] Credenciais só de desenvolvimento num `.env.example` versionado. O `.env` real fica no `.gitignore`, e qualquer segredo real vai para os Codespaces secrets.
- [x] Extensões: C# Dev Kit e um cliente de Postgres.
- [x] Máquina de 2 cores para começar. Suba para 4 só se Testcontainers ficar lento, porque a cota gratuita é consumida proporcionalmente aos cores.
- [x] Idle timeout de 30 min e retenção curta documentados no README. É configuração da conta de quem usa, não do repositório.
- [x] RabbitMQ **não** entra agora. Ele é adicionado ao compose no M5.
- [ ] Opcional: rodar o CI dentro do mesmo devcontainer (`devcontainers/ci`), garantindo que o CI e o ambiente de desenvolvimento sejam idênticos.

**Critério do passo:** num repositório recém-clonado, "Open in Codespaces" → `dotnet test` passa sem instalar nada à mão. O README ganha o badge "Open in GitHub Codespaces".

Depois do ambiente pronto:
- [x] Solução .NET 10: `Banking.Api`, `Banking.Domain`, `Banking.Application`, `Banking.Infrastructure`, `Banking.Contracts`, mais `Banking.ArchitectureTests` e `Banking.IntegrationTests`. `Banking.UnitTests` nasce no M2, junto com o primeiro código de domínio.
- [x] O mesmo compose do devcontainer serve de referência para quem quiser rodar localmente com Docker/Colima. Não mantenha dois arquivos divergentes.
- [x] Migrations aplicadas pela role `migrator`, com a app conectando como `app` (sem DDL).
- [x] CI no GitHub Actions: build, testes (Testcontainers), gitleaks, CodeQL e Dependabot.
- [x] ArchitectureTests: Domain não referencia EF, ASP.NET nem Infrastructure.
- [x] `TimeProvider` injetado. Nenhum `DateTime.UtcNow` no domínio.

**Critério:** CI verde num PR; a app sobe, conecta como `app` e recebe erro ao tentar `DROP TABLE`.

### M2: Ledger (o coração) (5 a 8 dias)
- [x] VOs `Money` e `Currency`. Sem construtor público que aceite estado inválido. `Money` é classe, então `default` é `null` e o compilador acusa (ADR-002).
- [x] `LedgerAccount`, `LedgerTransaction` e `LedgerEntry` com `account_sequence` e `balance_after_minor` nas contas de cliente.
- [x] `account_balances` atualizada na mesma transação, pelo trigger `ledger.apply_entry` (ADR-008).
- [x] No Postgres:
  - CHECK `amount > 0`;
  - constraint trigger de balanceamento;
  - trigger anti UPDATE/DELETE;
  - REVOKE;
  - `UNIQUE(account_id, sequence)`;
  - `UNIQUE(reverses_transaction_id)`.
- [x] Reversão como nova transação linkada.
- [x] **Property-based tests** (FsCheck/CsCheck): para qualquer sequência de postings válidos, Σ D = Σ C e o saldo projetado bate com Σ entries.
- [x] Teste de integração: `INSERT` desbalanceado direto via SQL falha no commit, e `UPDATE` num entry falha.

**Critério:** é impossível, mesmo via SQL cru com a role `app`, criar um ledger inconsistente.

### M3: Identidade, Customer, Account, Deposit (4 a 6 dias)
- [ ] JWT/OIDC com Keycloak, com roles `customer`, `operator` e `admin`.
- [ ] Customer com CPF validado, criptografado e com blind index. CPF mascarado nos logs.
- [ ] Account com vínculo ao `LedgerAccount` e status (Active, Blocked, Closed).
- [ ] Deposit: só `operator`, com Idempotency-Key, lançando `D Funding / C Cliente`, limite e motivo.
- [ ] `GET /balance` com `ledgerBalance` e `availableBalance` (iguais na Fase 1, ver ADR-006).
- [ ] Autorização por recurso em todos os GET. Testes de BOLA (A lê a conta de B → 404).

**Critério:** um cliente só enxerga o que é dele, e só o operador cria dinheiro, sempre com contrapartida.

### M4: Transferência interna + idempotência + concorrência (5 a 8 dias)
É o milestone mais importante do projeto.
- [ ] `POST /transfers` (interna), com `currency` e `Idempotency-Key` obrigatórios.
- [ ] Tabela de idempotência `UNIQUE(client_id, operation, key)`, gravada na mesma transação, e `RequestHash` sobre JSON canônico. Mesma chave com payload diferente → 409 (erro de conflito).
- [ ] Lock `FOR UPDATE` ordenado, status e saldo checados dentro do lock, `lock_timeout` configurado.
- [ ] Limites por transação e diário.
- [ ] **Testes-demo** (integração, Postgres real):
  - [ ] saldo 100, duas transferências de 80 simultâneas → 1 sucesso e 1 INSUFFICIENT_FUNDS;
  - [ ] saldo 1.000, 100 transferências de 100 → exatamente 10 sucessos, saldo 0, trial balance = 0;
  - [ ] 100 requisições com a **mesma** chave → 1 LedgerTransaction e 100 respostas idênticas;
  - [ ] A→B e B→A em loop concorrente → nenhum deadlock;
  - [ ] A tenta debitar a conta de B → 404 e nada escrito.
- [ ] ADR-004 atualizada com latência e throughput medidos (k6 ou NBomber).

**Critério:** o cenário principal da spec (§27) roda com um comando e passa sempre (rodar 50× no CI para caçar flakiness).

### M5: Outbox, Inbox, RabbitMQ (4 a 6 dias)
- [ ] RabbitMQ adicionado ao compose do devcontainer, com usuário e senha próprios (nada de `guest/guest`) e porta de management privada.
- [ ] `OutboxMessage` gravada na mesma transação, com `NextAttemptAt`, `RetryCount` e status `Pending/Published/DeadLettered`.
- [ ] Worker com `SKIP LOCKED`, backoff exponencial com jitter e limpeza periódica.
- [ ] **Consumidor real:** um read model de extrato (`account_statement`) ou um módulo de notificações, com **inbox** `UNIQUE(consumer, event_id)`.
- [ ] Eventos versionados (`TransferCompleted.v1`), sem PII.
- [ ] Testes:
  - [ ] broker fora do ar → transferências continuam funcionando, eventos ficam pendentes e são publicados quando o broker volta;
  - [ ] evento entregue duas vezes → efeito aplicado uma vez;
  - [ ] app morta entre o commit e a publicação → o evento sai após o restart.

**Critério:** nenhum evento é perdido e nenhum é aplicado duas vezes, com o broker ou a app caindo no meio.

### M6: Transferência externa, Mock Provider, UNKNOWN, reconciliação (6 a 10 dias)
- [ ] Contrato `IBankingProvider` com `SubmitTransferAsync`, `GetTransferStatusAsync` e callback/webhook simulado.
- [ ] Mock com cenários **por requisição**: SUCCESS, FAILED, TIMEOUT, HTTP500, DUPLICATE, UNKNOWN, e sucesso tardio (timeout agora, sucesso visível depois).
- [ ] Saga:
  - Tx1: `D Cliente / C Clearing`, estado CREATED, outbox. O worker grava UNKNOWN antes de chamar o provider;
  - worker chama o provider fora da transação, com `transferId` como chave;
  - Tx2: confirma ou estorna.
- [ ] Polly: timeout, retry **só** em erros seguros e circuit breaker.
- [ ] UNKNOWN nunca vira FAILED automaticamente. O job de reconciliação consulta o status e resolve.
- [ ] Mock gera extrato, e a reconciliação diária compara settlement com o extrato.
- [ ] Testes para cada cenário, incluindo "timeout, mas o provider processou", que precisa terminar COMPLETED **sem débito duplo**.

**Critério:** para cada cenário do mock, o estado final e os lançamentos são os esperados, e o saldo de clearing = Σ operações pendentes.

### M7: Observabilidade e auditoria (3 a 5 dias)
- [ ] OpenTelemetry (traces, métricas e logs) no Aspire Dashboard, com trace atravessando API → DB → outbox → consumidor (propagando `traceparent` na mensagem).
- [ ] Métricas da spec **com fonte real**: `ledger.balance_mismatch` vem do job de reconciliação, e `outbox.pending` é um gauge da tabela.
- [ ] Serilog estruturado, com destructuring policy mascarando CPF.
- [ ] Audit log com hash chain e job de verificação.
- [ ] `/health` (liveness) e `/ready` (Postgres, e broker como degradado, não como falha).

**Critério:** dado um `transferId`, você encontra o trace completo, o audit log e os eventos em menos de 1 minuto.

### M8: Hardening e portfólio (3 a 5 dias)
- [ ] Maker-checker para estorno e depósito acima do limite.
- [ ] Rate limiting por `sub`.
- [ ] Trivy e SBOM no CI.
- [ ] Threat model v1, cada ameaça com o teste que a cobre.
- [ ] README com:
  - diagrama;
  - "o que este projeto prova", com links para os testes;
  - como rodar a demo em 1 comando;
  - trade-offs e o que ficou de fora (e por quê).
- [ ] Script de demo (`make demo`) rodando os cenários do §27 e do §25 com saída legível.
- [ ] Vídeo/GIF de 2 minutos: concorrência, idempotência, broker caindo e audit log detectando adulteração.

---

## Parte 4: Definition of Done (verificável)

Cada item aponta para um teste automatizado.

| # | Critério | Prova |
|---|---|---|
| 1 | Nenhum lançamento desbalanceado persiste, nem via SQL cru | Teste de integração com a role `app` |
| 2 | Ledger imutável no banco | Teste de UPDATE/DELETE falhando |
| 3 | 100 × R$100 com saldo R$1.000 → 10 sucessos, saldo 0 | Teste de concorrência (rodado N vezes) |
| 4 | Mesma Idempotency-Key × 100 → 1 operação | Teste de integração |
| 5 | Chave reutilizada com payload diferente → 409 | Teste de integração |
| 6 | Usuário não move nem lê recurso alheio | Testes BOLA |
| 7 | Só `operator` deposita, sempre com contrapartida | Teste de autorização e do ledger |
| 8 | Broker fora → nenhum evento perdido | Teste com o container parado |
| 9 | Evento duplicado → efeito único | Teste de inbox |
| 10 | Timeout do provider → UNKNOWN → reconciliado sem débito duplo | Teste da saga |
| 11 | Trial balance = 0 e projeção = ledger | Job de reconciliação + teste |
| 12 | Adulteração do audit log é detectada | Teste da hash chain |
| 13 | Nenhum CPF em log ou evento | Teste que captura os logs e procura o padrão |
| 14 | Domain sem dependências de infra | ArchitectureTests |

---

## Parte 5: Riscos do projeto (não técnicos)

- **Escopo é o maior risco.** A spec original tem uns 15 subsistemas. Projetos de portfólio morrem no M5 de 8. Se o tempo apertar, **M0 a M4 bem feitos valem mais que M0 a M8 pela metade**. M4 sozinho já é um portfólio forte.
- **Não copie jargão sem prova.** Recrutadores técnicos abrem os testes. Cada palavra do README ("resiliente", "idempotente") precisa de um teste que a demonstre.
- **Documente o que você NÃO fez e por quê.** Mostra mais senioridade do que fingir completude.
- **Fase 2 (BaaS real):** não assuma que um sandbox de BaaS vai aceitar uma pessoa física. Verifique cedo, porque o contrato do M6 pode precisar de ajuste.
