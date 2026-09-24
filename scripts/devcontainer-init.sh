#!/usr/bin/env bash
# Roda na máquina que hospeda o devcontainer, antes do compose subir.
# No Codespaces, o navegador acessa o Keycloak pela URL da porta encaminhada, que só se conhece aqui.
set -euo pipefail

env_file="$(dirname "$0")/../.devcontainer/.env"
touch "$env_file"

if [[ -n "${CODESPACE_NAME:-}" && -n "${GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN:-}" ]] && ! grep -q '^KEYCLOAK_PUBLIC_URL=' "$env_file"; then
  echo "KEYCLOAK_PUBLIC_URL=https://${CODESPACE_NAME}-8080.${GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN}" >> "$env_file"
fi
