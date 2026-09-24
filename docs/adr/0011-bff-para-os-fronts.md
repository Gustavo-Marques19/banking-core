# ADR-011: BFF para os fronts

- Status: aceita
- Data: 2026-09-24

## Contexto

Os fronts rodam no navegador e precisam chamar a API com um token do Keycloak. Guardar access token e refresh token no navegador (localStorage, sessionStorage ou memória do JavaScript) deixa a credencial ao alcance de qualquer XSS, e um token roubado funciona de qualquer lugar até expirar.

## Decisão

Um backend-for-frontend (BFF) em ASP.NET Core (`src/Banking.Bff`). O código é um só; cada front roda a sua instância, com configuração própria:

| | Backoffice | App do cliente |
|---|---|---|
| Porta em desenvolvimento | 5180 | 5190 |
| Cliente no Keycloak | `banking-backoffice` | `banking-web` |
| Cookie | `__Host-backoffice` | `__Host-banking` |
| Papéis aceitos (`Bff:AllowedRoles`) | `operator`, `admin` | `customer` |
| Arquivos (`ASPNETCORE_WEBROOT`) | `web/backoffice/dist` | `web/customer/dist` |

- **Login:** OIDC com authorization code e PKCE S256, com cliente confidencial. O BFF troca o código pelos tokens no backchannel.
- **Sessão:** os tokens ficam no ticket da sessão, criptografado pelo Data Protection. O navegador recebe só o cookie da instância, com `HttpOnly`, `Secure` e `SameSite=Strict`. O BFF não sobe com cookie sem o prefixo `__Host-`.
- **Chamadas:** `/api/*` passa pelo BFF (YARP), que remove o cookie e o header CSRF e acrescenta `Authorization: Bearer` com o token do usuário. Token perto de expirar é renovado pelo refresh token, também no servidor; se a renovação falhar, a sessão acaba.
- **CSRF:** toda requisição que muda estado em `/api` ou `/bff` exige o header `X-CSRF: 1`. Outro site não consegue enviá-lo sem CORS, e o BFF não habilita CORS.
- **Headers:** CSP sem `unsafe-inline`, `frame-ancestors 'none'`, `nosniff`, `Referrer-Policy: no-referrer`, HSTS fora de desenvolvimento. Só em desenvolvimento a CSP aceita script e estilo inline, que o Vite usa para o hot reload; o build de produção não tem nada inline, e os E2E rodam com a CSP estrita.
- **Acesso:** só os papéis da instância entram; qualquer outro recebe 403 em tudo além do login. Sem sessão, `/api` e `/bff` respondem 401 (o front decide ir ao login); as outras rotas redirecionam para o login.
- **Logout:** `POST /bff/logout` (com CSRF) encerra a sessão local e devolve a URL de logout do Keycloak, com `id_token_hint`.

## Alternativas consideradas

- **SPA com PKCE e token no navegador.** Menos peças, mas qualquer XSS lê o token. Para um backoffice que aprova depósitos e estornos, ou um app que transfere dinheiro, o risco não compensa.
- **Duende BFF.** Resolve tudo isso pronto, mas tem licença comercial. As peças necessárias aqui (OIDC, cookie, YARP, header CSRF) cabem em pouco código e ficam visíveis para quem lê o projeto.
- **Next.js como BFF.** Traria um segundo runtime de servidor. O BFF em .NET usa as mesmas bibliotecas de autenticação da API.

## Consequências

- Duas instâncias em vez de dois projetos: uma correção de segurança no BFF vale para os dois fronts. O que muda entre eles é configuração, testada em `BffTests` com as duas.
- Um cliente OIDC por front: revogar ou trocar o segredo de um não derruba o outro, e o operador que entra no app do cliente recebe 403 mesmo com login válido.

- O cookie carrega os tokens criptografados, e fica maior (é dividido em partes). Com mais usuários, um store de sessão no servidor reduz o cookie a um id.
- As chaves do Data Protection são efêmeras em desenvolvimento: reiniciar o BFF encerra as sessões. Em produção, precisam ser persistidas e protegidas.
- O front nunca lida com token: `fetch` com `credentials: 'same-origin'` e o header `X-CSRF` em POST.
- Testes de integração fazem o login real no Keycloak (formulário incluso) e provam que nenhuma resposta ao navegador contém token.
