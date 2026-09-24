using Banking.Domain.Ledger;

namespace Banking.Infrastructure.Persistence.Migrations;

/// <summary>
/// Travas do ledger no banco (ADR-008). Valem para qualquer cliente SQL, não só para a aplicação.
/// </summary>
internal static class LedgerDatabaseObjects
{
    public static readonly string Create = $$"""
        -- Marca em qual transação de banco a transação contábil nasceu. Lançamentos só entram nela.
        ALTER TABLE ledger.ledger_transactions ADD COLUMN created_xact xid8 NOT NULL;

        CREATE FUNCTION ledger.stamp_transaction() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog, pg_temp AS $fn$
        BEGIN
            NEW.created_xact := pg_current_xact_id();
            RETURN NEW;
        END $fn$;

        CREATE TRIGGER ledger_transactions_stamp BEFORE INSERT ON ledger.ledger_transactions
            FOR EACH ROW EXECUTE FUNCTION ledger.stamp_transaction();

        -- Sequência e saldo são calculados aqui. O que a aplicação enviar nessas colunas é descartado.
        CREATE FUNCTION ledger.apply_entry() RETURNS trigger
        LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
        DECLARE
            v_created_xact xid8;
            v_normal_balance char(1);
            v_sequence bigint;
            v_balance bigint;
        BEGIN
            SELECT created_xact INTO v_created_xact FROM ledger.ledger_transactions WHERE id = NEW.transaction_id;
            IF v_created_xact IS DISTINCT FROM pg_current_xact_id() THEN
                RAISE EXCEPTION 'Lançamentos só entram na transação de banco que criou a transação contábil %.', NEW.transaction_id
                    USING ERRCODE = 'integrity_constraint_violation';
            END IF;

            SELECT normal_balance INTO v_normal_balance
              FROM ledger.account_balances
             WHERE ledger_account_id = NEW.ledger_account_id
               FOR UPDATE;

            IF FOUND THEN
                UPDATE ledger.account_balances
                   SET balance_minor = balance_minor
                           + CASE WHEN NEW.direction = v_normal_balance THEN NEW.amount_minor ELSE -NEW.amount_minor END,
                       last_sequence = last_sequence + 1,
                       updated_at = clock_timestamp()
                 WHERE ledger_account_id = NEW.ledger_account_id
                RETURNING last_sequence, balance_minor INTO v_sequence, v_balance;

                NEW.account_sequence := v_sequence;
                NEW.balance_after_minor := v_balance;
            ELSE
                NEW.account_sequence := NULL;
                NEW.balance_after_minor := NULL;
            END IF;

            RETURN NEW;
        END $fn$;

        CREATE TRIGGER ledger_entries_apply BEFORE INSERT ON ledger.ledger_entries
            FOR EACH ROW EXECUTE FUNCTION ledger.apply_entry();

        -- Σ débitos = Σ créditos por moeda, com pelo menos dois lançamentos. Verificado no commit.
        CREATE FUNCTION ledger.assert_transaction_balanced() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog, pg_temp AS $fn$
        DECLARE
            v_transaction_id uuid;
            v_entries integer;
        BEGIN
            IF TG_TABLE_NAME = 'ledger_transactions' THEN
                v_transaction_id := NEW.id;
            ELSE
                v_transaction_id := NEW.transaction_id;
            END IF;

            SELECT count(*) INTO v_entries FROM ledger.ledger_entries WHERE transaction_id = v_transaction_id;
            IF v_entries < 2 THEN
                RAISE EXCEPTION 'Transação contábil % tem % lançamento(s); o mínimo é 2.', v_transaction_id, v_entries
                    USING ERRCODE = 'integrity_constraint_violation';
            END IF;

            IF EXISTS (
                SELECT 1
                  FROM ledger.ledger_entries
                 WHERE transaction_id = v_transaction_id
                 GROUP BY currency
                HAVING sum(CASE WHEN direction = 'D' THEN amount_minor ELSE -amount_minor END) <> 0) THEN
                RAISE EXCEPTION 'Transação contábil % não está balanceada.', v_transaction_id
                    USING ERRCODE = 'integrity_constraint_violation';
            END IF;

            RETURN NULL;
        END $fn$;

        CREATE CONSTRAINT TRIGGER ledger_transactions_balanced AFTER INSERT ON ledger.ledger_transactions
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ledger.assert_transaction_balanced();

        CREATE CONSTRAINT TRIGGER ledger_entries_balanced AFTER INSERT ON ledger.ledger_entries
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ledger.assert_transaction_balanced();

        -- Append-only, inclusive para o dono das tabelas.
        CREATE FUNCTION ledger.reject_modification() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog, pg_temp AS $fn$
        BEGIN
            RAISE EXCEPTION 'O ledger é append-only: % em ledger.% não é permitido.', TG_OP, TG_TABLE_NAME
                USING ERRCODE = 'integrity_constraint_violation';
        END $fn$;

        CREATE TRIGGER ledger_accounts_append_only BEFORE UPDATE OR DELETE ON ledger.ledger_accounts
            FOR EACH ROW EXECUTE FUNCTION ledger.reject_modification();
        CREATE TRIGGER ledger_accounts_no_truncate BEFORE TRUNCATE ON ledger.ledger_accounts
            FOR EACH STATEMENT EXECUTE FUNCTION ledger.reject_modification();
        CREATE TRIGGER ledger_transactions_append_only BEFORE UPDATE OR DELETE ON ledger.ledger_transactions
            FOR EACH ROW EXECUTE FUNCTION ledger.reject_modification();
        CREATE TRIGGER ledger_transactions_no_truncate BEFORE TRUNCATE ON ledger.ledger_transactions
            FOR EACH STATEMENT EXECUTE FUNCTION ledger.reject_modification();
        CREATE TRIGGER ledger_entries_append_only BEFORE UPDATE OR DELETE ON ledger.ledger_entries
            FOR EACH ROW EXECUTE FUNCTION ledger.reject_modification();
        CREATE TRIGGER ledger_entries_no_truncate BEFORE TRUNCATE ON ledger.ledger_entries
            FOR EACH STATEMENT EXECUTE FUNCTION ledger.reject_modification();

        -- Saldo materializado: nasce zerado e só muda por dentro de ledger.apply_entry().
        CREATE FUNCTION ledger.guard_account_balance() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog, pg_temp AS $fn$
        BEGIN
            IF TG_OP = 'INSERT' THEN
                NEW.balance_minor := 0;
                NEW.last_sequence := 0;
                RETURN NEW;
            END IF;

            IF TG_OP = 'UPDATE' AND pg_trigger_depth() > 1 THEN
                RETURN NEW;
            END IF;

            RAISE EXCEPTION 'Saldo só muda por lançamento no ledger: % em ledger.account_balances não é permitido.', TG_OP
                USING ERRCODE = 'integrity_constraint_violation';
        END $fn$;

        CREATE TRIGGER account_balances_guard BEFORE INSERT OR UPDATE OR DELETE ON ledger.account_balances
            FOR EACH ROW EXECUTE FUNCTION ledger.guard_account_balance();
        CREATE TRIGGER account_balances_no_truncate BEFORE TRUNCATE ON ledger.account_balances
            FOR EACH STATEMENT EXECUTE FUNCTION ledger.reject_modification();

        -- Conta de cliente ganha linha de saldo automaticamente. Conta de sistema só por migration.
        CREATE FUNCTION ledger.open_ledger_account() RETURNS trigger
        LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
        BEGIN
            IF NEW.account_id IS NULL AND session_user = '{{DatabaseRoles.App}}' THEN
                RAISE EXCEPTION 'Contas de sistema só são criadas por migration.'
                    USING ERRCODE = 'insufficient_privilege';
            END IF;

            IF NEW.has_materialized_balance THEN
                INSERT INTO ledger.account_balances
                    (ledger_account_id, currency, normal_balance, balance_minor, last_sequence, allow_negative, updated_at)
                VALUES (NEW.id, NEW.currency, NEW.normal_balance, 0, 0, NEW.allow_negative, clock_timestamp());
            END IF;

            RETURN NULL;
        END $fn$;

        CREATE TRIGGER ledger_accounts_open AFTER INSERT ON ledger.ledger_accounts
            FOR EACH ROW EXECUTE FUNCTION ledger.open_ledger_account();

        -- A aplicação trava saldos sem ter UPDATE na tabela.
        CREATE FUNCTION ledger.lock_balances(p_ledger_account_ids uuid[]) RETURNS SETOF ledger.account_balances
        LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
            SELECT *
              FROM ledger.account_balances
             WHERE ledger_account_id = ANY(p_ledger_account_ids)
             ORDER BY ledger_account_id
               FOR UPDATE
        $fn$;

        REVOKE ALL ON FUNCTION ledger.stamp_transaction(), ledger.apply_entry(), ledger.assert_transaction_balanced(),
            ledger.reject_modification(), ledger.guard_account_balance(), ledger.open_ledger_account(),
            ledger.lock_balances(uuid[]) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION ledger.lock_balances(uuid[]) TO {{DatabaseRoles.App}};

        REVOKE INSERT ON ledger.account_balances FROM {{DatabaseRoles.App}};

        INSERT INTO ledger.ledger_accounts
            (id, code, name, nature, normal_balance, currency, account_id, allow_negative, has_materialized_balance, created_at)
        VALUES
            ('{{SystemLedgerAccounts.Funding}}', '1.1.01', 'Funding simulado', 'ASSET', 'D', 'BRL', NULL, true, false, now()),
            ('{{SystemLedgerAccounts.Settlement}}', '1.1.02', 'Settlement no provider', 'ASSET', 'D', 'BRL', NULL, true, false, now()),
            ('{{SystemLedgerAccounts.Clearing}}', '2.2.01', 'Clearing de transferências externas', 'LIABILITY', 'C', 'BRL', NULL, false, false, now());
        """;

    public const string Drop = """
        DROP FUNCTION IF EXISTS ledger.lock_balances(uuid[]);
        DROP FUNCTION IF EXISTS ledger.open_ledger_account();
        DROP FUNCTION IF EXISTS ledger.guard_account_balance();
        DROP FUNCTION IF EXISTS ledger.reject_modification();
        DROP FUNCTION IF EXISTS ledger.assert_transaction_balanced();
        DROP FUNCTION IF EXISTS ledger.apply_entry();
        DROP FUNCTION IF EXISTS ledger.stamp_transaction();
        """;
}
