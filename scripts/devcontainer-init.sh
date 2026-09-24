#!/usr/bin/env bash
# Roda na máquina que hospeda o devcontainer, antes do compose subir.
# No Codespaces, o navegador chega a cada porta por uma URL própria, que só se conhece aqui. O Keycloak precisa dela
# como emissor e como endereço de retorno dos dois fronts; os BFFs, para montar esses retornos (ADR-011).
set -euo pipefail

env_file="$(dirname "$0")/../.devcontainer/.env"
touch "$env_file"

if [[ -n "${CODESPACE_NAME:-}" && -n "${GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN:-}" ]]; then
  for entry in KEYCLOAK_PUBLIC_URL=8080 BACKOFFICE_PUBLIC_URL=5180 CUSTOMER_PUBLIC_URL=5190; do
    name="${entry%%=*}"
    port="${entry##*=}"
    if ! grep -q "^$name=" "$env_file"; then
      echo "$name=https://${CODESPACE_NAME}-${port}.${GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN}" >> "$env_file"
    fi
  done
fi
