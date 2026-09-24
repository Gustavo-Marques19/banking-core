#!/usr/bin/env bash
# Sobe a API e os dois fronts no devcontainer (ou no Codespaces), com o build de produção de cada front servido pelo
# seu BFF. Ctrl+C derruba tudo. Para hot reload, use os perfis de src/Banking.Bff/Properties/launchSettings.json.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
logs=/tmp/banking-dev
mkdir -p "$logs"
pids=()

cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
}
trap cleanup EXIT INT TERM

wait_for() {
  for _ in $(seq 1 90); do
    curl -fsS "$1" >/dev/null 2>&1 && return 0
    sleep 2
  done
  echo "Não respondeu: $1 (logs em $logs)" >&2
  return 1
}

echo "Preparando: migrations, build dos fronts, da API e do BFF..."
./scripts/migrate.sh >"$logs/migrate.log" 2>&1
[[ -d web/node_modules ]] || (cd web && npm ci --no-audit --no-fund >"$logs/npm.log" 2>&1)
(cd web && npm run build --workspace backoffice --workspace customer >"$logs/build-web.log" 2>&1)
dotnet build src/Banking.Api --verbosity quiet >"$logs/build-api.log" 2>&1
dotnet build src/Banking.Bff --verbosity quiet >"$logs/build-bff.log" 2>&1

ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 \
  dotnet run --project src/Banking.Api --no-build --no-launch-profile >"$logs/api.log" 2>&1 &
pids+=($!)

# Mesmo BFF, duas instâncias (ADR-011). Autoridade, metadados e API vêm do docker-compose do devcontainer.
start_bff() {
  local name="$1" port="$2" client="$3" secret="$4" roles="$5" cookie="$6" public_url="$7"
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="http://localhost:$port" \
  ASPNETCORE_WEBROOT="$root/web/$name/dist" \
  Bff__ClientId="$client" \
  Bff__ClientSecret="$secret" \
  Bff__AllowedRoles="$roles" \
  Bff__CookieName="$cookie" \
  Bff__PublicUrl="$public_url" \
    dotnet run --project src/Banking.Bff --no-build --no-launch-profile >"$logs/bff-$name.log" 2>&1 &
  pids+=($!)
}
start_bff backoffice 5180 banking-backoffice backoffice-dev-only "operator,admin" __Host-backoffice "${BACKOFFICE_PUBLIC_URL:-}"
start_bff customer 5190 banking-web web-dev-only customer __Host-banking "${CUSTOMER_PUBLIC_URL:-}"

wait_for http://localhost:5080/health
wait_for http://localhost:5180/health
wait_for http://localhost:5190/health

cat <<INFO

Pronto. Senha de todo usuário: <usuario>-dev-only.

  App do cliente  ${CUSTOMER_PUBLIC_URL:-http://localhost:5190}   alice, bruno, carla (sem cadastro)
  Backoffice      ${BACKOFFICE_PUBLIC_URL:-http://localhost:5180}   olga e otto (operadores), ada (admin)

Dinheiro numa conta, depositado pela operadora olga:  ./scripts/deposit.sh carla 1000
Logs em $logs. Ctrl+C encerra.
INFO

wait
