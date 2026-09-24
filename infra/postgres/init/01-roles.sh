#!/bin/sh
# Roda uma vez, quando o volume do Postgres é criado. Usado pelo devcontainer e pelos testes de integração.
set -e

: "${BANKING_MIGRATOR_PASSWORD:?defina BANKING_MIGRATOR_PASSWORD}"
: "${BANKING_APP_PASSWORD:?defina BANKING_APP_PASSWORD}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  --set=migrator_password="$BANKING_MIGRATOR_PASSWORD" \
  --set=app_password="$BANKING_APP_PASSWORD" <<'SQL'
CREATE ROLE banking_migrator LOGIN PASSWORD :'migrator_password';
CREATE ROLE banking_app LOGIN PASSWORD :'app_password';
CREATE DATABASE banking OWNER banking_migrator;
SQL

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname banking <<'SQL'
REVOKE ALL ON DATABASE banking FROM PUBLIC;
GRANT CONNECT ON DATABASE banking TO banking_app;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
SQL
