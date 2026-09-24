#!/usr/bin/env bash
# Deposita na primeira conta de um cliente, como a operadora olga. Acima de R$ 10.000 o depósito fica esperando a
# aprovação de outro operador no backoffice (entre como otto).
# Uso: ./scripts/deposit.sh <usuario> <valor>    Exemplo: ./scripts/deposit.sh carla 1000.00
set -euo pipefail

user="${1:?Uso: $0 <usuario> <valor>}"
amount="${2:?Uso: $0 <usuario> <valor>}"
keycloak="${KEYCLOAK_URL:-http://keycloak:8080}"
api="${API_URL:-http://localhost:5080}"

if [[ ! "$amount" =~ ^[0-9]+(\.[0-9]{1,2})?$ ]]; then
  echo "Valor no formato da API, com ponto: 1000 ou 1000.50" >&2
  exit 1
fi

token() {
  curl -fsS -d grant_type=password -d client_id=banking-cli -d "username=$1" -d "password=$1-dev-only" \
    "$keycloak/realms/banking/protocol/openid-connect/token" | sed -E 's/.*"access_token":"([^"]+)".*/\1/'
}

accounts=$(curl -fsS -H "Authorization: Bearer $(token "$user")" "$api/api/v1/accounts")
if [[ "$accounts" == "[]" ]]; then
  echo "$user ainda não tem conta. Abra pelo app do cliente primeiro." >&2
  exit 1
fi

account=$(sed -E 's/^\[\{"id":"([^"]+)".*/\1/' <<<"$accounts")
key=$(cat /proc/sys/kernel/random/uuid 2>/dev/null || uuidgen)
curl -sS -X POST "$api/api/v1/accounts/$account/deposits" \
  -H "Authorization: Bearer $(token olga)" -H "Idempotency-Key: $key" -H "Content-Type: application/json" \
  -d "{\"amount\":\"$amount\",\"currency\":\"BRL\",\"reason\":\"depósito de teste pelo scripts/deposit.sh\"}" \
  -w "\nHTTP %{http_code}\n"
