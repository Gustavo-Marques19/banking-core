# ADR-009: Autenticação e autorização

- Status: aceita
- Data: 2026-09-24

## Contexto

A spec original não tinha autenticação nem autorização: qualquer pessoa com `curl` criava dinheiro e movia o de outra. Para um projeto cujo objetivo declarado é segurança e controle de operações, esse é o primeiro buraco a fechar.

## Decisão

**Identidade**
- Keycloak como provedor OIDC, com o realm versionado em `.devcontainer/keycloak/realm-banking.json`. Nada de login e senha próprios.
- A API valida JWT:
  - só RS256, com as chaves vindas do JWKS do realm;
  - `iss`, `aud` (`banking-api`) e `exp` validados;
  - token HMAC ou sem assinatura é recusado (threat model T18).
- Roles numa claim plana `roles`, emitida por um mapper do realm: `customer`, `operator`, `admin`.
- Toda rota exige token, por política de fallback. As exceções são explícitas: `/health`, `/ready`, OpenAPI em desenvolvimento e o webhook do provider, que se autentica por HMAC.

**Autorização**
- Por papel, nos endpoints: depósito e aprovações para `operator`; verificação da auditoria e cenário do mock para `admin`.
- Por recurso, nos casos de uso:
  - o cliente é resolvido pelo `sub` do token;
  - conta, extrato, transferência e cadastro de outra pessoa respondem **404**, igual a recurso inexistente, para não confirmar que o id existe (threat model T2);
  - só o dono movimenta a conta, nem o operador debita conta de cliente por transferência.
- O `Actor` vem sempre do token, nunca de header ou corpo. É ele que vai para a auditoria e para o escopo da idempotência.

**Separação de funções (maker-checker)**
- Depósito acima de `Limits:DepositApprovalThreshold` e estorno de transferência precisam de um segundo operador. Quem pediu não aprova, e a regra fica no domínio, não no endpoint.

## Alternativas consideradas

- **Autenticação própria (usuário e senha na API).** Mais código sensível para manter (hash de senha, bloqueio, recuperação) sem ganho para o objetivo do projeto.
- **403 para recurso de outra pessoa.** É mais informativo, mas confirma que o id existe. O 404 não vaza isso.
- **Autorização só por atributo no endpoint.** Não resolve BOLA, porque a regra depende de quem é o dono do recurso, e isso só se sabe consultando o banco.

## Consequências

- Os testes de integração emitem tokens com uma chave própria, usando a mesma validação de produção. Um teste separado usa um Keycloak real (Testcontainers) com o realm versionado.
- No devcontainer, o emissor é fixo no endereço que o navegador vê (`KC_HOSTNAME`), e quem chama pelo endereço interno recebe URLs internas (`KC_HOSTNAME_BACKCHANNEL_DYNAMIC`). A API busca os metadados em `http://keycloak:8080` e valida o `iss` público. (Ajuste do F1, para o login pelo navegador do backoffice.)
- O cliente `banking-cli` usa password grant, que é só para desenvolvimento. Um front-end real usaria authorization code com PKCE.
