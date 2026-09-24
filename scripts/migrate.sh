#!/usr/bin/env bash
# Aplica as migrations com a role banking_migrator (BANKING_MIGRATOR_CONNECTION). A API nunca migra.
set -euo pipefail

: "${BANKING_MIGRATOR_CONNECTION:?defina BANKING_MIGRATOR_CONNECTION}"

dotnet ef database update \
  --project src/Banking.Infrastructure \
  --startup-project src/Banking.Infrastructure \
  --connection "$BANKING_MIGRATOR_CONNECTION"
