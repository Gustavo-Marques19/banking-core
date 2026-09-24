#!/usr/bin/env bash
# Gera as chaves de PII do ambiente de desenvolvimento em user-secrets, fora do repositório. Idempotente.
set -euo pipefail

project=src/Banking.Api
existing=$(dotnet user-secrets list --project "$project" 2>/dev/null || true)

for key in Pii:EncryptionKey Pii:BlindIndexKey Provider:WebhookSecret; do
  if ! grep -q "^$key = " <<<"$existing"; then
    dotnet user-secrets set "$key" "$(openssl rand -base64 32)" --project "$project" >/dev/null
    echo "gerada $key"
  fi
done
