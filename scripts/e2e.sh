#!/usr/bin/env bash
# Testes E2E do backoffice: Postgres e Keycloak em containers, API e BFF de verdade, build de produção do front.
# Precisa de Docker, .NET 10 e Node. Usado pelo CI e roda igual no Codespaces.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
compose=(docker compose -f infra/e2e/docker-compose.e2e.yml)
logs="$root/web/backoffice/test-results"
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

(cd web/backoffice && npm ci --no-audit --no-fund && npm run build)
dotnet build src/Banking.Api --configuration Release --verbosity quiet
dotnet build src/Banking.Backoffice.Bff --configuration Release --verbosity quiet

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
ExternalTransfers__PollInterval=00:00:00.500 \
ExternalTransfers__CheckInterval=00:00:01 \
ExternalTransfers__MaxSubmitAttempts=2 \
Provider__Timeout=00:00:01 \
  dotnet run --project src/Banking.Api --configuration Release --no-build --no-launch-profile >"$logs/api.log" 2>&1 &
pids+=($!)

ASPNETCORE_ENVIRONMENT=E2E \
ASPNETCORE_URLS=http://localhost:5180 \
Bff__Authority=http://localhost:8080/realms/banking \
Bff__ClientSecret=backoffice-dev-only \
Bff__RequireHttpsMetadata=false \
Bff__ApiBaseUrl=http://localhost:5080 \
  dotnet run --project src/Banking.Backoffice.Bff --configuration Release --no-build --no-launch-profile >"$logs/bff.log" 2>&1 &
pids+=($!)

wait_for http://localhost:5080/health
wait_for http://localhost:5180/health

cd web/backoffice
npx playwright install --with-deps chromium >/dev/null
npx playwright test
