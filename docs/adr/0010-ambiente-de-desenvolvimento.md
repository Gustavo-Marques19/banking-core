# ADR-010: Ambiente de desenvolvimento

- Status: aceita
- Data: 2026-09-24

## Contexto

A máquina de desenvolvimento tem pouco espaço livre em disco e não tem Docker. O projeto precisa de Postgres, Keycloak e, a partir do M5, RabbitMQ, além de Docker para os Testcontainers. A Vercel foi cogitada como alternativa.

## Decisão

- Desenvolvimento no GitHub Codespaces, com o ambiente descrito em `.devcontainer/` (compose com app, Postgres e Keycloak, mais a feature `docker-in-docker` para os Testcontainers).
- CI no GitHub Actions, cujos runners Linux já têm Docker.
- O mesmo compose serve para quem quiser rodar localmente com Docker ou Colima. Não existe um segundo arquivo de compose.
- Demo pública, se houver, fica para o M8, num host de containers.

## Alternativas consideradas

- **Vercel.** Não roda .NET como runtime oficial, não mantém processos contínuos (o worker do outbox e o de reconciliação), não hospeda RabbitMQ nem Keycloak e não oferece Docker para os testes. Serviria para um front-end, que está fora da Fase 1.
- **Local com Docker Desktop.** Ocupa vários GB, e o disco já está 94% cheio.
- **Local com Colima.** Viável, mas no limite do espaço livre. Fica como opção, não como padrão.

## Consequências

- A cota gratuita do Codespaces é consumida por hora de uso e proporcionalmente aos cores: começar com 2 cores, configurar idle timeout curto e apagar codespaces parados.
- Todas as portas encaminhadas ficam privadas.
- O repositório precisa estar no GitHub para usar Codespaces e Actions. Até lá, o M0 roda só com git local.
