#!/usr/bin/env bash
# Testes E2E dos dois fronts: Postgres, Keycloak e RabbitMQ em containers, API e duas instâncias do BFF de verdade,
# build de produção de cada front. Precisa de Docker, .NET 10 e Node. Usado pelo CI e roda igual no Codespaces.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
compose=(docker compose -f infra/e2e/docker-compose.e2e.yml)
logs="$root/web/test-results"
mkdir -p "$logs"
pids=()

cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
  "${compose[@]}" down -v >/dev/null 2>&1 || true
}
trap cleanup EXIT

wait_for() {
  for _ in $(seq 1 90); do
    curl -fsS "$1" >/dev/null 2>&1 && return 0
    sleep 2
  done
  echo "Não respondeu: $1" >&2
  return 1
}

"${compose[@]}" up -d --wait
wait_for http://localhost:8080/realms/banking/.well-known/openid-configuration

(cd web && npm ci --no-audit --no-fund && npm run build --workspace backoffice --workspace customer)
dotnet build src/Banking.Api --configuration Release --verbosity quiet
dotnet build src/Banking.Bff --configuration Release --verbosity quiet

export BANKING_MIGRATOR_CONNECTION="Host=localhost;Database=banking;Username=banking_migrator;Password=migrator-e2e"
dotnet tool restore
./scripts/migrate.sh

ASPNETCORE_ENVIRONMENT=E2E \
ASPNETCORE_URLS=http://localhost:5080 \
ConnectionStrings__Banking="Host=localhost;Database=banking;Username=banking_app;Password=app-e2e" \
Pii__EncryptionKey="$(openssl rand -base64 32)" \
Pii__BlindIndexKey="$(openssl rand -base64 32)" \
Auth__Authority=http://localhost:8080/realms/banking \
Auth__RequireHttpsMetadata=false \
Messaging__Uri=amqp://banking:rabbit-e2e@localhost:5672/ \
ExternalTransfers__PollInterval=00:00:00.500 \
ExternalTransfers__CheckInterval=00:00:01 \
ExternalTransfers__MaxSubmitAttempts=2 \
Provider__Timeout=00:00:01 \
  dotnet run --project src/Banking.Api --configuration Release --no-build --no-launch-profile >"$logs/api.log" 2>&1 &
pids+=($!)

# Mesmo BFF, duas instâncias: cada front com o próprio cliente OIDC, cookie, papéis e arquivos (ADR-011).
start_bff() {
  local name="$1" port="$2" client="$3" secret="$4" roles="$5" cookie="$6"
  ASPNETCORE_ENVIRONMENT=E2E \
  ASPNETCORE_URLS="http://localhost:$port" \
  ASPNETCORE_WEBROOT="$root/web/$name/dist" \
  Bff__Authority=http://localhost:8080/realms/banking \
  Bff__ClientId="$client" \
  Bff__ClientSecret="$secret" \
  Bff__AllowedRoles="$roles" \
  Bff__CookieName="$cookie" \
  Bff__RequireHttpsMetadata=false \
  Bff__ApiBaseUrl=http://localhost:5080 \
    dotnet run --project src/Banking.Bff --configuration Release --no-build --no-launch-profile >"$logs/bff-$name.log" 2>&1 &
  pids+=($!)
}
start_bff backoffice 5180 banking-backoffice backoffice-dev-only "operator,admin" __Host-backoffice
start_bff customer 5190 banking-web web-dev-only customer __Host-banking

wait_for http://localhost:5080/health
wait_for http://localhost:5180/health
wait_for http://localhost:5190/health

cd web
npx playwright install --with-deps chromium >/dev/null
# Os dois roteiros rodam mesmo que o primeiro falhe; o script falha se qualquer um falhar.
status=0
(cd backoffice && npx playwright test) || status=$?
(cd customer && npx playwright test) || status=$?
exit "$status"
