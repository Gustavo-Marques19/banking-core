# Fase 1 --- Resilient Banking Core

## Objetivo

Construir uma primeira versão de um **Banking Core resiliente**,
inicialmente sem dependência de um BaaS real.

A aplicação deve simular operações bancárias com dinheiro fictício, mas
já possuir as propriedades arquiteturais necessárias para, em uma fase
posterior, integrar um BaaS real em sandbox.

A prioridade desta fase é:

-   consistência financeira;
-   idempotência;
-   concorrência;
-   double-spending prevention;
-   ledger imutável;
-   double-entry bookkeeping;
-   Outbox Pattern;
-   eventos;
-   tolerância a falhas;
-   abstração de provider externo;
-   testes automatizados.

------------------------------------------------------------------------

# 1. Escopo

A Fase 1 deve implementar:

1.  Customer
2.  Account
3.  Ledger
4.  Deposits
5.  Transfers
6.  Idempotency
7.  Concurrency control
8.  Outbox
9.  Domain events
10. Mock Banking Provider
11. Estados de processamento
12. Auditoria básica
13. Testes unitários
14. Testes de integração
15. Observabilidade inicial

Fora do escopo inicial:

-   Pix real;
-   BaaS real;
-   cartões;
-   KYC/KYB real;
-   Open Finance;
-   antifraude avançado;
-   produção;
-   múltiplas moedas com FX;
-   microserviços independentes.

------------------------------------------------------------------------

# 2. Princípio arquitetural

A aplicação deve ser inicialmente um **Modular Monolith**, evitando
microserviços prematuros.

Arquitetura:

``` text
                         React / Client
                               │
                               ▼
                        ┌─────────────┐
                        │ Banking API │
                        │ ASP.NET     │
                        └──────┬──────┘
                               │
                     ┌─────────▼─────────┐
                     │    Application    │
                     │      Layer        │
                     └─────────┬─────────┘
                               │
                     ┌─────────▼─────────┐
                     │      Domain       │
                     │                   │
                     │ Customer          │
                     │ Account           │
                     │ Ledger            │
                     │ Payment           │
                     │ Transaction       │
                     └─────────┬─────────┘
                               │
                     ┌─────────▼─────────┐
                     │  Infrastructure   │
                     │                   │
                     │ PostgreSQL        │
                     │ Redis             │
                     │ Outbox            │
                     │ Message Broker    │
                     └───────────────────┘
```

A separação de módulos deve permitir uma futura extração para
microserviços, mas isso não é requisito da Fase 1.

------------------------------------------------------------------------

# 3. Stack proposta

## Backend

-   .NET 10
-   ASP.NET Core
-   Entity Framework Core
-   C#

## Banco

Preferencialmente:

-   PostgreSQL

Alternativa:

-   SQL Server

## Infraestrutura

-   Docker
-   Docker Compose
-   Redis
-   RabbitMQ ou Azure Service Bus

## Observabilidade

-   OpenTelemetry
-   Serilog
-   Prometheus/Grafana ou Application Insights

## Testes

-   xUnit
-   Testcontainers
-   testes unitários
-   testes de integração
-   testes de concorrência

------------------------------------------------------------------------

# 4. Estrutura do projeto

``` text
/src

  Banking.Api

  Banking.Domain

  Banking.Application

  Banking.Infrastructure

  Banking.Contracts

/tests

  Banking.UnitTests

  Banking.IntegrationTests

  Banking.ArchitectureTests

/docker

  docker-compose.yml
```

## Banking.Domain

Deve conter apenas regras de negócio.

Não deve conhecer:

-   EF Core;
-   Redis;
-   RabbitMQ;
-   HTTP;
-   BaaS;
-   Azure;
-   infraestrutura.

Principais conceitos:

``` text
Customer
Account
Money
LedgerTransaction
LedgerEntry
Payment
Transaction
```

------------------------------------------------------------------------

# 5. Customer

Modelo inicial:

``` text
Customer

Id
Document
Name
Status
CreatedAt
```

Exemplo:

``` json
{
  "id": "c9f...",
  "document": "12345678900",
  "name": "João",
  "status": "Active"
}
```

Estados possíveis:

``` text
Active
Blocked
Closed
```

------------------------------------------------------------------------

# 6. Account

Modelo inicial:

``` text
Account

Id
CustomerId
Number
Branch
Currency
Status
CreatedAt
```

A conta não deve depender exclusivamente de um campo `Balance` como
fonte de verdade.

O **Ledger é a fonte de verdade financeira**.

Um saldo materializado pode existir futuramente para performance, mas
deverá ser reconciliável com o Ledger.

------------------------------------------------------------------------

# 7. Money

Criar um Value Object para representar valores monetários.

Exemplo conceitual:

``` csharp
public readonly record struct Money(
    decimal Amount,
    string Currency);
```

Regras:

-   valores precisam respeitar a moeda;
-   operações entre moedas diferentes devem ser explicitamente
    proibidas;
-   não permitir valores inválidos;
-   evitar manipulação de dinheiro como `decimal` espalhado pela
    aplicação.

Exemplo inválido:

``` text
BRL 100 + USD 10
```

sem conversão explícita.

------------------------------------------------------------------------

# 8. Ledger

O Ledger é o núcleo financeiro da aplicação.

Uma transferência de R\$ 100 deve gerar dois lançamentos:

``` text
Transaction: TX123

Account A
DEBIT  100

Account B
CREDIT 100
```

## LedgerTransaction

``` text
Id
ExternalId
Type
Status
CorrelationId
CreatedAt
```

## LedgerEntry

``` text
Id
TransactionId
AccountId
EntryType
Amount
CreatedAt
```

O Ledger deve ser tratado como **append-only / imutável**.

Não alterar uma transação financeira já registrada.

Correções devem gerar novos lançamentos.

Exemplo:

``` text
Original:
DEBIT 100

Reversal:
CREDIT 100
```

------------------------------------------------------------------------

# 9. Double-entry bookkeeping

Toda transação financeira deve obedecer:

``` text
SUM(DEBITS) == SUM(CREDITS)
```

Exemplo válido:

``` text
Account A
DEBIT  100

Account B
CREDIT 100
```

Exemplo inválido:

``` text
Account A
DEBIT 100

Account B
CREDIT 90
```

Essa invariável deve ser protegida pelo domínio e pelo banco quando
aplicável.

------------------------------------------------------------------------

# 10. Deposit

Como a Fase 1 não utiliza um BaaS real, deve existir uma operação para
adicionar dinheiro fictício.

Endpoint:

``` http
POST /api/v1/accounts/{accountId}/deposits
```

Body:

``` json
{
  "amount": 1000.00,
  "currency": "BRL"
}
```

O depósito deve gerar um lançamento no Ledger.

Conceitualmente:

``` text
Deposit
   ↓
LedgerTransaction
   ↓
CREDIT 1000
```

O dinheiro deve ser identificado como originado de uma conta/sistema de
funding fictício.

------------------------------------------------------------------------

# 11. Transfer

Endpoint:

``` http
POST /api/v1/transfers
```

Body:

``` json
{
  "sourceAccountId": "...",
  "destinationAccountId": "...",
  "amount": 100.00
}
```

Header:

``` http
Idempotency-Key: 01JABC...
```

Fluxo esperado:

``` text
Request
   │
   ▼
Validate
   │
   ▼
Idempotency
   │
   ▼
Concurrency Control
   │
   ▼
Check Funds
   │
   ▼
Create Transaction
   │
   ├── DEBIT source
   │
   └── CREDIT destination
   │
   ▼
Create Outbox Event
   │
   ▼
COMMIT
```

A operação financeira deve ser atômica.

------------------------------------------------------------------------

# 12. Idempotência

Idempotência é requisito obrigatório.

Cenário:

``` text
Client
  │
  ├── Request ABC123
  │
  ▼
Server
  │
  ├── Processa
  ├── Commit
  └── Response perdida
```

O cliente repete:

``` text
Request ABC123
```

O sistema deve retornar o resultado original, sem criar outra transação.

Tabela conceitual:

``` text
IdempotencyKey

Key
RequestHash
Response
Status
CreatedAt
ExpiresAt
```

Deve existir uma constraint única:

``` text
UNIQUE(Key)
```

Também deve ser validado se o mesmo Idempotency-Key foi reutilizado com
um payload diferente.

Nesse caso, retornar erro de conflito.

------------------------------------------------------------------------

# 13. Double spending

Cenário obrigatório de teste:

``` text
Saldo = R$100

Request A → R$80
Request B → R$80
```

Resultado correto:

``` text
A → SUCCESS
B → INSUFFICIENT_FUNDS
```

Nunca:

``` text
A → SUCCESS
B → SUCCESS

Saldo = -R$60
```

A implementação pode utilizar:

-   pessimistic locking;
-   optimistic concurrency;
-   transações;
-   constraints;
-   ou combinação dessas técnicas.

A decisão deve ser documentada.

------------------------------------------------------------------------

# 14. Concorrência

Testar explicitamente múltiplas operações simultâneas na mesma conta.

Exemplo:

``` text
Saldo inicial: R$1.000

100 requests simultâneos
cada um tentando transferir R$100
```

Resultado esperado:

``` text
10 SUCCESS
90 INSUFFICIENT_FUNDS
```

Nunca deve existir:

``` text
saldo negativo
```

nem inconsistência no Ledger.

------------------------------------------------------------------------

# 15. Outbox Pattern

Eventos não devem depender de uma chamada externa dentro da mesma
transação.

Não fazer:

``` text
Database COMMIT
      ↓
RabbitMQ
```

porque:

``` text
Database = SUCCESS
RabbitMQ = FAILURE
```

pode resultar em perda do evento.

Implementar:

``` text
BEGIN TRANSACTION

LedgerTransaction
LedgerEntries
OutboxMessage

COMMIT
```

Tabela:

``` text
OutboxMessage

Id
Type
Payload
OccurredAt
ProcessedAt
RetryCount
Status
```

Depois:

``` text
Outbox Worker
      ↓
Message Broker
```

Se o broker estiver indisponível, os eventos permanecem pendentes.

------------------------------------------------------------------------

# 16. Eventos

Eventos iniciais:

``` text
CustomerCreated
AccountCreated
MoneyDeposited
TransferCreated
TransferCompleted
TransferFailed
LedgerEntryCreated
```

Exemplo:

``` json
{
  "eventId": "evt-123",
  "eventType": "TransferCompleted",
  "occurredAt": "2026-09-24T00:00:00Z",
  "transactionId": "tx-123",
  "sourceAccountId": "...",
  "destinationAccountId": "...",
  "amount": 100.00,
  "currency": "BRL"
}
```

Todo evento deve possuir identificador único.

------------------------------------------------------------------------

# 17. Mock Banking Provider

A aplicação deve possuir uma abstração para um provider financeiro
externo.

Interface conceitual:

``` csharp
public interface IBankingProvider
{
    Task<ProviderTransferResult> TransferAsync(
        ProviderTransferRequest request,
        CancellationToken cancellationToken);
}
```

Implementação inicial:

``` text
IBankingProvider
       │
       └── MockBankingProvider
```

Posteriormente:

``` text
IBankingProvider
       │
       ├── MockBankingProvider
       │
       └── CelcoinBankingProvider
```

O objetivo é permitir que a Fase 2 adicione um BaaS real sem modificar o
domínio.

------------------------------------------------------------------------

# 18. Simulação de falhas do Provider

O Mock Provider deve conseguir simular:

``` text
SUCCESS
FAILED
TIMEOUT
HTTP 500
DUPLICATE
UNKNOWN
```

Pode existir uma configuração administrativa:

``` http
POST /admin/provider/scenario
```

Exemplo:

``` json
{
  "scenario": "TIMEOUT"
}
```

Isso permitirá testar a resiliência sem depender de uma falha real.

------------------------------------------------------------------------

# 19. Estados de Payment/Transfer

Evitar apenas:

``` text
SUCCESS
FAILED
```

Usar estados como:

``` text
CREATED
PROCESSING
COMPLETED
FAILED
UNKNOWN
CANCELLED
```

O estado `UNKNOWN` é especialmente importante.

Exemplo:

``` text
Seu sistema
     │
     ▼
Provider
     │
     X
   timeout
```

O sistema não sabe se o provider processou ou não.

Portanto:

``` text
UNKNOWN
```

não deve ser automaticamente convertido em `FAILED`.

Esse estado será utilizado posteriormente pelo mecanismo de
reconciliação.

------------------------------------------------------------------------

# 20. Redis

Redis não deve ser a fonte de verdade financeira.

Uso inicial:

``` text
Idempotency Cache
Rate Limiting
Distributed Lock
Caching
```

A aplicação deve continuar correta mesmo se Redis ficar indisponível,
sempre que possível.

A consistência financeira deve continuar dependente do banco
transacional.

Teste futuro:

``` text
docker stop redis
```

e verificar o comportamento da aplicação.

------------------------------------------------------------------------

# 21. API inicial

Endpoints:

``` text
POST   /api/v1/customers
GET    /api/v1/customers/{id}

POST   /api/v1/accounts
GET    /api/v1/accounts/{id}
GET    /api/v1/accounts/{id}/balance
GET    /api/v1/accounts/{id}/transactions

POST   /api/v1/accounts/{id}/deposits

POST   /api/v1/transfers
GET    /api/v1/transfers/{id}

GET    /api/v1/ledger/transactions/{id}

GET    /health
GET    /ready
```

Endpoints administrativos do Mock Provider devem ficar claramente
separados da API de negócio.

------------------------------------------------------------------------

# 22. Auditoria

Registrar pelo menos:

``` text
CorrelationId
TraceId
CustomerId
AccountId
TransactionId
RequestId
EventId
CreatedAt
Actor
Operation
Status
```

O log não deve conter dados sensíveis desnecessariamente.

------------------------------------------------------------------------

# 23. Observabilidade inicial

Implementar:

## Logs

Eventos importantes:

``` text
TransferCreated
TransferCompleted
TransferFailed
IdempotencyHit
ConcurrencyConflict
OutboxPublished
OutboxRetry
ProviderTimeout
ProviderError
```

## Métricas

``` text
transfer.success
transfer.failed
transfer.pending
transfer.unknown
transfer.duplicate

outbox.pending
outbox.retry

ledger.balance_mismatch

provider.latency
provider.error
provider.timeout
```

## Tracing

Fluxo:

``` text
POST /transfers
      │
      ├── Account
      ├── Ledger
      ├── Outbox
      └── Provider
```

Manter:

``` text
TraceId
CorrelationId
TransactionId
```

------------------------------------------------------------------------

# 24. Testes unitários

Criar testes para:

``` text
MoneyTests
AccountTests
LedgerTests
TransferTests
IdempotencyTests
ConcurrencyTests
```

Casos importantes:

-   valor negativo;
-   valor zero;
-   moeda incompatível;
-   saldo insuficiente;
-   transferência para a própria conta;
-   transferência duplicada;
-   lançamento inválido;
-   débito sem crédito correspondente;
-   crédito sem débito correspondente.

------------------------------------------------------------------------

# 25. Testes de integração

Utilizar Testcontainers para subir:

``` text
PostgreSQL
Redis
RabbitMQ
```

Testar:

``` text
API
 ↓
Database
 ↓
Outbox
 ↓
Broker
```

Cenários:

1.  transferência normal;
2.  transferência duplicada;
3.  concorrência;
4.  broker indisponível;
5.  Redis indisponível;
6.  provider timeout;
7.  provider 500;
8.  restart da aplicação;
9.  processamento duplicado do evento.

------------------------------------------------------------------------

# 26. Definition of Done

A Fase 1 será considerada concluída quando:

``` text
[ ] Criar Customer
[ ] Criar Account
[ ] Depositar dinheiro fictício
[ ] Consultar saldo
[ ] Consultar transações
[ ] Fazer transferência
[ ] Double-entry ledger
[ ] Ledger imutável
[ ] Idempotência
[ ] Concurrency control
[ ] Proteção contra double spending
[ ] Outbox Pattern
[ ] Domain events
[ ] Mock Banking Provider
[ ] Provider timeout
[ ] Provider failure
[ ] UNKNOWN state
[ ] Retry
[ ] Auditoria
[ ] Logs estruturados
[ ] Tracing
[ ] Métricas
[ ] Testes unitários
[ ] Testes de integração
[ ] Teste de concorrência
```

------------------------------------------------------------------------

# 27. Cenário de demonstração principal

A PoC deve conseguir demonstrar:

``` text
Saldo inicial
R$ 1.000
```

Executar:

``` text
100 transferências simultâneas
R$ 100 cada
```

Resultado esperado:

``` text
10 transferências concluídas
90 recusadas por saldo insuficiente

Saldo final:
R$ 0

Ledger:
Débitos = Créditos

Sem saldo negativo
Sem duplicação
Sem inconsistência
```

Depois repetir o cenário utilizando:

``` text
mesma Idempotency-Key
```

Resultado:

``` text
100 requests
1 operação financeira
100 respostas equivalentes
```

------------------------------------------------------------------------

# 28. Preparação para Fase 2

A Fase 1 deve terminar com uma fronteira clara:

``` text
                    Banking Core
                         │
                         ▼
                 IBankingProvider
                         │
             ┌───────────┴───────────┐
             │                       │
             ▼                       ▼
      MockBankingProvider      RealBaasProvider
                                  │
                                  ▼
                              BaaS Sandbox
```

O domínio não deve conhecer o fornecedor.

Exemplo:

``` text
Domain
  NÃO conhece:
    Celcoin
    Dock
    Pismo
    HTTP
    REST
```

Somente a infraestrutura conhece o provider concreto.

------------------------------------------------------------------------

# 29. Ordem recomendada de implementação

## Milestone 1 --- Foundation

``` text
[ ] Criar solução .NET
[ ] Configurar Docker Compose
[ ] PostgreSQL
[ ] EF Core
[ ] Migrations
[ ] Estrutura Domain/Application/Infrastructure/API
```

## Milestone 2 --- Banking Core

``` text
[ ] Customer
[ ] Account
[ ] Money
[ ] Ledger
[ ] Deposit
[ ] Balance
```

## Milestone 3 --- Transfers

``` text
[ ] Transfer
[ ] Double-entry
[ ] Transaction boundaries
[ ] Insufficient funds
[ ] Concurrency
```

## Milestone 4 --- Resilience

``` text
[ ] Idempotency
[ ] Outbox
[ ] Events
[ ] Message Broker
[ ] Retry
```

## Milestone 5 --- External Provider

``` text
[ ] IBankingProvider
[ ] Mock Provider
[ ] SUCCESS
[ ] FAILURE
[ ] TIMEOUT
[ ] UNKNOWN
```

## Milestone 6 --- Observability

``` text
[ ] Structured logs
[ ] OpenTelemetry
[ ] Metrics
[ ] Tracing
```

## Milestone 7 --- Validation

``` text
[ ] Unit tests
[ ] Integration tests
[ ] Concurrency tests
[ ] Idempotency tests
[ ] Failure tests
[ ] Restart tests
```

------------------------------------------------------------------------

# 30. Resultado esperado

Ao final da Fase 1 deverá existir uma aplicação que não seja apenas um
CRUD bancário.

Ela deverá demonstrar:

``` text
Consistência
     +
Idempotência
     +
Concorrência
     +
Double-entry Ledger
     +
Event-driven architecture
     +
Outbox
     +
Resiliência
     +
Observabilidade
```

O sistema deve estar preparado para que, na Fase 2, um provider real de
Banking as a Service seja conectado atrás de `IBankingProvider`,
permitindo testar a arquitetura contra um ambiente de sandbox sem
alterar o núcleo financeiro da aplicação.
