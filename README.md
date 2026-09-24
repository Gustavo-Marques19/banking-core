# Banking Core

Núcleo bancário com dinheiro fictício, construído para estudo e portfólio. O foco é consistência financeira, segurança e controle das operações, não quantidade de funcionalidades.

[![Open in GitHub Codespaces](https://github.com/codespaces/badge.svg)](https://codespaces.new/Gustavo-Marques19/banking-core?quickstart=1)

## Status

Fase 1. M0 (design) concluído, M1 (fundação) em andamento: ambiente, solução .NET 10, roles do banco e CI. Ainda não há regra de negócio.

## Como rodar

### No Codespaces

1. Abra pelo botão acima. O ambiente sobe Postgres e Keycloak, restaura os pacotes e aplica as migrations.
2. `dotnet run --project src/Banking.Api` sobe a API na porta 5080, com `/health`, `/ready` e `/openapi/v1.json`.
3. `dotnet test` roda todos os testes. Os de integração criam o próprio Postgres com Testcontainers.

Para não gastar a cota gratuita à toa, em [github.com/settings/codespaces](https://github.com/settings/codespaces) defina o idle timeout em 30 minutos e uma retenção curta para codespaces parados.

### Localmente, sem Docker

Dá para compilar e rodar os testes de arquitetura:

```sh
dotnet build
dotnet test --project tests/Banking.ArchitectureTests
```

Os testes de integração precisam de Docker e rodam no Codespaces ou no CI.

## Banco de dados

| Role | Uso | Pode |
|---|---|---|
| `banking_migrator` | migrations (`./scripts/migrate.sh`) | dona dos schemas `accounts`, `ledger`, `payments` e `platform` |
| `banking_app` | API e workers | ler e gravar dados, sem DDL; no `ledger`, só `SELECT` e `INSERT` |

As roles são criadas por [`infra/postgres/init/01-roles.sh`](infra/postgres/init/01-roles.sh), o mesmo script usado pelo devcontainer e pelos testes de integração. Os testes provam que a role da aplicação não cria, não apaga e não lê o histórico de migrations.

## Onde está cada coisa

- [Plano da Fase 1](docs/plano-fase-1.md): crítica da spec original, marcos e Definition of Done.
- [Decisões de arquitetura](docs/adr/README.md).
- [Lançamentos por cenário](docs/ledger/lancamentos.md).
- [Máquinas de estado](docs/estados.md).
- [Threat model](docs/threat-model.md).
- [Spec original](docs/spec/fase-1-original.md), mantida como referência histórica.
