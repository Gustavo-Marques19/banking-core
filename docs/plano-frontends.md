# Plano dos fronts

Depois da Fase 1 do backend (M0 a M8). Primeiro o backoffice, depois o app do cliente.

## Decisões

- **BFF em ASP.NET Core.** O navegador nunca recebe token. O BFF faz o login OIDC (authorization code com PKCE), guarda os tokens do lado do servidor e entrega um cookie `HttpOnly`, `Secure`, `SameSite=Strict`, com proteção anti-CSRF. As chamadas passam pelo BFF, que as repassa à API com o token. Um XSS não consegue roubar credencial. (ADR-011)
- **React com TypeScript e Vite.** Tipos gerados do OpenAPI da API. Dinheiro trafega como string decimal, igual à API, e nunca vira `number` (ADR-002).
- **Dois fronts, dois clientes no Keycloak.** Backoffice e app do cliente ficam separados: sessões, permissões e riscos diferentes.
- **Direção visual por front:** [web/backoffice/DESIGN.md](../web/backoffice/DESIGN.md). O app do cliente terá a sua.

## Marcos

### F1: preparar o backend
- [x] Listagens para as filas do operador: depósitos pendentes, estornos pendentes, transferências externas em revisão manual.
- [x] Resolução manual de transferência externa em UNKNOWN, com maker-checker: um operador registra o desfecho confirmado com o provider e a evidência, outro aprova. Hoje o M6 marca a revisão, mas não existe como concluí-la.
- [x] Cliente `banking-backoffice` no Keycloak (authorization code com PKCE, confidencial, usado pelo BFF).
- [x] Hostname do Keycloak: o endereço que o navegador vê é diferente do que a API usa (ADR-009). Emissor fixo e metadados pelo endereço interno: `KC_HOSTNAME` com o endereço público e `KC_HOSTNAME_BACKCHANNEL_DYNAMIC`. No Codespaces, `scripts/devcontainer-init.sh` descobre a URL encaminhada; esse caminho não foi verificado num Codespace real.

### F2: BFF do backoffice
- [x] Login OIDC, sessão por cookie, logout.
- [x] Anti-CSRF, CSP e headers de segurança.
- [x] Proxy para a API com o token do usuário (YARP).
- [x] Testes: o navegador nunca recebe token; requisição sem CSRF é recusada; só `operator` e `admin` entram.

### F3: backoffice
- [ ] Fila de aprovações (depósitos e estornos), com "quem pediu não aprova" visível antes do clique.
- [ ] Transferências em revisão manual, com a resolução do F1.
- [ ] Reconciliação: rodar, ver divergências.
- [ ] Auditoria por recurso, verificação da cadeia (admin) e busca por trace id.
- [ ] Estados de vazio, carregando e erro em toda tela.
- [ ] Testes E2E (Playwright) contra o ambiente completo e checagem de acessibilidade (axe).

### F4: app do cliente
- [ ] Direção visual própria (nova rodada de perguntas).
- [ ] Contas, saldo, extrato, notificações.
- [ ] Transferência interna e externa com tela de confirmação e uma chave de idempotência por intenção de pagamento.
- [ ] Acompanhamento da transferência externa até o estado final.
- [ ] E2E e acessibilidade.

## Riscos

- A Fase 1 tem sete PRs sem revisão humana. Mudança de contrato na revisão pode quebrar o front.
- Front é outra habilidade e pode diluir o foco. O backoffice é o que mais conversa com a tese do projeto (controles operacionais).
