# Plano dos fronts

Depois da Fase 1 do backend (M0 a M8). Primeiro o backoffice, depois o app do cliente.

## Decisões

- **BFF em ASP.NET Core.** O navegador nunca recebe token. O BFF faz o login OIDC (authorization code com PKCE), guarda os tokens do lado do servidor e entrega um cookie `HttpOnly`, `Secure`, `SameSite=Strict`, com proteção anti-CSRF. As chamadas passam pelo BFF, que as repassa à API com o token. Um XSS não consegue roubar credencial. (ADR-011)
- **React com TypeScript e Vite.** Tipos escritos à mão: as respostas da API não têm schema no OpenAPI (os endpoints devolvem `IResult`), então a geração sairia vazia. Dinheiro trafega como string decimal, igual à API, e nunca vira `number` (ADR-002).
- **Dois fronts, dois clientes no Keycloak.** Backoffice e app do cliente ficam separados: sessões, permissões e riscos diferentes.
- **Direção visual por front:** [web/backoffice/DESIGN.md](../web/backoffice/DESIGN.md) e [web/customer/DESIGN.md](../web/customer/DESIGN.md).
- **Um BFF, duas instâncias.** O F2 nasceu como BFF do backoffice; no F4 virou `src/Banking.Bff`, configurado por instância (ADR-011).
- **Workspace npm em `web/`.** Cliente HTTP, formatação e leitura de dinheiro, estados de tela e diálogo de confirmação ficam em `web/shared`, usados pelos dois fronts.

## Marcos

### F1: preparar o backend
- [x] Listagens para as filas do operador: depósitos pendentes, estornos pendentes, transferências externas em revisão manual.
- [x] Resolução manual de transferência externa em UNKNOWN, com maker-checker: um operador registra o desfecho confirmado com o provider e a evidência, outro aprova. Hoje o M6 marca a revisão, mas não existe como concluí-la.
- [x] Cliente `banking-backoffice` no Keycloak (authorization code com PKCE, confidencial, usado pelo BFF).
- [x] Hostname do Keycloak: o endereço que o navegador vê é diferente do que a API usa (ADR-009). Emissor fixo e metadados pelo endereço interno: `KC_HOSTNAME` com o endereço público e `KC_HOSTNAME_BACKCHANNEL_DYNAMIC`. No Codespaces, `scripts/devcontainer-init.sh` descobre as URLs encaminhadas do Keycloak e dos dois fronts, e o BFF usa a do seu front nos retornos de login e logout (`Bff:PublicUrl`). Verificado num Codespace real em 2026-09-24, pelo navegador.

### F2: BFF do backoffice
- [x] Login OIDC, sessão por cookie, logout.
- [x] Anti-CSRF, CSP e headers de segurança.
- [x] Proxy para a API com o token do usuário (YARP).
- [x] Testes: o navegador nunca recebe token; requisição sem CSRF é recusada; só `operator` e `admin` entram.

### F3: backoffice
- [x] Fila de aprovações (depósitos e estornos), com "quem pediu não aprova" visível antes do clique.
- [x] Transferências em revisão manual, com a resolução do F1.
- [x] Reconciliação: rodar, ver divergências.
- [x] Auditoria por recurso e verificação da cadeia (admin). Busca por trace id ficou de fora: a API filtra só por recurso; o trace id aparece em cada registro.
- [x] Estados de vazio, carregando e erro em toda tela.
- [x] Testes E2E (Playwright) contra o ambiente completo e checagem de acessibilidade (axe).

### F4: app do cliente
- [x] Direção visual própria (nova rodada de perguntas): tema claro, mesmo acento do backoffice.
- [x] Backend: busca de conta de destino por agência e número, com titular mascarado e limite próprio (threat model T22); cliente `banking-web` no Keycloak.
- [x] Cadastro e abertura de conta pelo app.
- [x] Contas, saldo, extrato com "carregar mais", notificações.
- [x] Transferência interna e externa com tela de confirmação e uma chave de idempotência por intenção de pagamento (T23). Valor digitado em pt-BR e convertido para o formato da API sem passar por `number`.
- [x] Acompanhamento da transferência externa até o estado final, com cancelamento antes do envio.
- [x] E2E e acessibilidade: cadastro, transferência, resposta perdida na rede, clique duplo, recusa, transferência externa concluída e recusada, avisos pelo RabbitMQ, operador barrado, nenhum token no navegador, celular a 375 px, axe.

## Riscos

- A Fase 1 tem sete PRs sem revisão humana. Mudança de contrato na revisão pode quebrar o front.
- Front é outra habilidade e pode diluir o foco. O backoffice é o que mais conversa com a tese do projeto (controles operacionais).
