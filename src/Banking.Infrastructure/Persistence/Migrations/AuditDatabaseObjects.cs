namespace Banking.Infrastructure.Persistence.Migrations;

/// <summary>
/// Trilha de auditoria com hash encadeado (threat model T6 e T10). O formato do payload tem que bater com
/// Banking.Infrastructure.Audit.AuditQueries, que verifica a cadeia de forma independente.
/// </summary>
internal static class AuditDatabaseObjects
{
    public static readonly string Create = $$"""
        ALTER TABLE platform.audit_log
            ADD COLUMN chain_position bigint NOT NULL,
            ADD COLUMN previous_hash bytea,
            ADD COLUMN hash bytea NOT NULL,
            ADD CONSTRAINT uq_audit_log_chain_position UNIQUE (chain_position);

        CREATE FUNCTION platform.audit_entry_payload(
            p_position bigint, p_occurred_at timestamptz, p_actor text, p_operation text, p_resource_type text,
            p_resource_id text, p_outcome text, p_correlation_id text, p_trace_id text, p_details jsonb) RETURNS bytea
        LANGUAGE sql STABLE SET search_path = pg_catalog, pg_temp AS $fn$
            SELECT convert_to(concat_ws(chr(31),
                p_position::text,
                to_char(p_occurred_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                p_actor, p_operation, p_resource_type, p_resource_id, p_outcome,
                coalesce(p_correlation_id, ''), coalesce(p_trace_id, ''), p_details::text), 'UTF8')
        $fn$;

        -- Um escritor por vez: a posição e o hash anterior vêm do último registro commitado.
        CREATE FUNCTION platform.chain_audit_entry() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog, pg_temp AS $fn$
        DECLARE
            v_position bigint;
            v_hash bytea;
        BEGIN
            PERFORM pg_advisory_xact_lock(7300421);
            SELECT chain_position, hash INTO v_position, v_hash
              FROM platform.audit_log ORDER BY chain_position DESC LIMIT 1;

            NEW.chain_position := coalesce(v_position, 0) + 1;
            NEW.previous_hash := v_hash;
            NEW.hash := sha256(coalesce(v_hash, '\x'::bytea) || platform.audit_entry_payload(
                NEW.chain_position, NEW.occurred_at, NEW.actor, NEW.operation, NEW.resource_type,
                NEW.resource_id, NEW.outcome, NEW.correlation_id, NEW.trace_id, NEW.details));
            RETURN NEW;
        END $fn$;

        CREATE TRIGGER audit_log_chain BEFORE INSERT ON platform.audit_log
            FOR EACH ROW EXECUTE FUNCTION platform.chain_audit_entry();

        CREATE FUNCTION platform.reject_audit_modification() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog, pg_temp AS $fn$
        BEGIN
            RAISE EXCEPTION 'A trilha de auditoria é append-only: % não é permitido.', TG_OP
                USING ERRCODE = 'integrity_constraint_violation';
        END $fn$;

        CREATE TRIGGER audit_log_append_only BEFORE UPDATE OR DELETE ON platform.audit_log
            FOR EACH ROW EXECUTE FUNCTION platform.reject_audit_modification();
        CREATE TRIGGER audit_log_no_truncate BEFORE TRUNCATE ON platform.audit_log
            FOR EACH STATEMENT EXECUTE FUNCTION platform.reject_audit_modification();

        REVOKE ALL ON FUNCTION platform.chain_audit_entry(), platform.reject_audit_modification() FROM PUBLIC;
        REVOKE UPDATE, DELETE, TRUNCATE ON platform.audit_log FROM {{DatabaseRoles.App}};
        """;

    public const string Drop = """
        DROP FUNCTION IF EXISTS platform.reject_audit_modification();
        DROP FUNCTION IF EXISTS platform.chain_audit_entry();
        DROP FUNCTION IF EXISTS platform.audit_entry_payload(bigint, timestamptz, text, text, text, text, text, text, text, jsonb);
        """;
}
