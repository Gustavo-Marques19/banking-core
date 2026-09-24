import { api, ApiError } from "@banking/web-shared/client";
import { currencySymbol, formatAmount, parseAmount } from "@banking/web-shared/money";
import { Fragment, useRef, useState, type FormEvent, type ReactNode, type RefObject } from "react";
import { Link, useNavigate } from "react-router";
import type { AccountLookupView, AccountView, ExternalTransferView, TransferView } from "../api/types";
import { accountLabel, useBank, useSelectedAccount } from "../bank";
import { messageFor } from "../lib/messages";
import { useFocusHeading } from "../lib/useFocusHeading";

type Kind = "internal" | "external";

interface Draft {
  kind: Kind;
  sourceId: string;
  amountText: string;
  branch: string;
  number: string;
  bank: string;
  externalBranch: string;
  externalAccount: string;
  description: string;
}

/** O que foi conferido e vai ser enviado. A chave de idempotência nasce aqui e vale só para esta intenção. */
interface Intent {
  key: string;
  source: AccountView;
  amount: string;
  destination: { kind: "internal"; account: AccountLookupView; description: string } | { kind: "external"; bank: string; branch: string; account: string };
}

type Step = { name: "form" } | { name: "review"; intent: Intent } | { name: "done"; intent: Intent; transfer: TransferView };

export function Transfer() {
  const { accounts } = useBank();
  const selected = useSelectedAccount();
  const navigate = useNavigate();
  const [draft, setDraft] = useState<Draft>({
    kind: "internal",
    sourceId: selected.id,
    amountText: "",
    branch: "0001",
    number: "",
    bank: "",
    externalBranch: "",
    externalAccount: "",
    description: "",
  });
  const [step, setStep] = useState<Step>({ name: "form" });
  const [errors, setErrors] = useState<Partial<Record<keyof Draft | "form", string>>>({});
  const [checking, setChecking] = useState(false);
  const heading = useFocusHeading<HTMLHeadingElement>(step.name);

  const source = accounts.find((a) => a.id === draft.sourceId) ?? selected;
  const set = (field: keyof Draft) => (value: string) => setDraft((d) => ({ ...d, [field]: value }));

  async function review(event: FormEvent) {
    event.preventDefault();
    const found: typeof errors = {};
    const amount = parseAmount(draft.amountText);
    if (!amount) {
      found.amountText = "Informe um valor maior que zero, com até 2 casas depois da vírgula. Exemplo: 150,00.";
    }

    if (draft.kind === "internal") {
      if (!/^\d{4}$/.test(draft.branch)) found.branch = "A agência tem 4 números.";
      if (!/^\d{1,8}$/.test(draft.number)) found.number = "A conta tem até 8 números, sem traço.";
      if (draft.description.length > 140) found.description = "A descrição tem no máximo 140 caracteres.";
    } else {
      if (!/^\d{8}$/.test(draft.bank)) found.bank = "O código do banco (ISPB) tem 8 números.";
      if (!/^\d{1,4}$/.test(draft.externalBranch)) found.externalBranch = "A agência tem até 4 números.";
      if (!/^[A-Za-z0-9-]{1,30}$/.test(draft.externalAccount)) found.externalAccount = "Use letras, números e traço, até 30 caracteres.";
    }

    setErrors(found);
    if (Object.keys(found).length > 0 || !amount) {
      return;
    }

    if (draft.kind === "external") {
      setStep({
        name: "review",
        intent: { key: crypto.randomUUID(), source, amount, destination: { kind: "external", bank: draft.bank, branch: draft.externalBranch, account: draft.externalAccount } },
      });
      return;
    }

    setChecking(true);
    try {
      const account = await api<AccountLookupView>(`/api/v1/accounts/lookup?branch=${draft.branch}&number=${draft.number}`);
      if (account.id === source.id) {
        setErrors({ number: "Essa é a conta de origem. Escolha outra conta de destino." });
        return;
      }

      setStep({
        name: "review",
        intent: { key: crypto.randomUUID(), source, amount, destination: { kind: "internal", account, description: draft.description.trim() } },
      });
    } catch (error) {
      setErrors({ number: error instanceof ApiError && error.status === 404 ? "Nenhuma conta com essa agência e número." : messageFor(error) });
    } finally {
      setChecking(false);
    }
  }

  if (step.name === "review") {
    return (
      <Review
        heading={heading}
        intent={step.intent}
        onBack={() => setStep({ name: "form" })}
        onInternalDone={(transfer) => setStep({ name: "done", intent: step.intent, transfer })}
        onExternalCreated={(transfer) => navigate(`/transferencias/externas/${transfer.id}`)}
      />
    );
  }

  if (step.name === "done") {
    return (
      <>
        <header className="page-header">
          <h1 ref={heading} tabIndex={-1}>
            Transferência concluída
          </h1>
          <p>O dinheiro já está na conta de destino.</p>
        </header>
        <div className="panel">
          <Facts intent={step.intent} />
          <div className="form__actions">
            <Link className="button button--primary" to="/">
              Ver extrato
            </Link>
            <button
              type="button"
              className="button"
              onClick={() => {
                setDraft((d) => ({ ...d, amountText: "", number: "", description: "" }));
                setStep({ name: "form" });
              }}
            >
              Nova transferência
            </button>
          </div>
        </div>
      </>
    );
  }

  return (
    <>
      <header className="page-header">
        <h1 ref={heading} tabIndex={-1}>
          Transferir
        </h1>
        <p>Você confere tudo numa tela de resumo antes de o dinheiro sair.</p>
      </header>

      <form className="form panel" onSubmit={review} noValidate>
        {accounts.length > 1 && (
          <div className="field">
            <label htmlFor="source">De</label>
            <select id="source" value={draft.sourceId} onChange={(e) => set("sourceId")(e.target.value)}>
              {accounts.map((a) => (
                <option key={a.id} value={a.id}>
                  {accountLabel(a)}
                </option>
              ))}
            </select>
          </div>
        )}

        <fieldset className="field">
          <legend>Para</legend>
          <div className="choices">
            <label>
              <input type="radio" name="kind" checked={draft.kind === "internal"} onChange={() => set("kind")("internal")} />
              Conta no Banking Core
            </label>
            <label>
              <input type="radio" name="kind" checked={draft.kind === "external"} onChange={() => set("kind")("external")} />
              Conta em outro banco
            </label>
          </div>
        </fieldset>

        {draft.kind === "internal" ? (
          <div className="form__row">
            <Field id="branch" label="Agência" value={draft.branch} onChange={set("branch")} error={errors.branch} inputMode="numeric" size="short" />
            <Field id="number" label="Conta" value={draft.number} onChange={set("number")} error={errors.number} inputMode="numeric" />
          </div>
        ) : (
          <>
            <Field
              id="bank"
              label="Banco (ISPB)"
              hint="Código de 8 números do banco de destino."
              value={draft.bank}
              onChange={set("bank")}
              error={errors.bank}
              inputMode="numeric"
            />
            <div className="form__row">
              <Field id="externalBranch" label="Agência" value={draft.externalBranch} onChange={set("externalBranch")} error={errors.externalBranch} inputMode="numeric" size="short" />
              <Field id="externalAccount" label="Conta" value={draft.externalAccount} onChange={set("externalAccount")} error={errors.externalAccount} />
            </div>
          </>
        )}

        <Field
          id="amount"
          label={`Valor (${currencySymbol(source.currency)})`}
          placeholder="0,00"
          value={draft.amountText}
          onChange={set("amountText")}
          error={errors.amountText}
          inputMode="decimal"
        />

        {draft.kind === "internal" && (
          <Field id="description" label="Descrição (opcional)" hint="Aparece no extrato das duas contas." value={draft.description} onChange={set("description")} error={errors.description} />
        )}

        <div className="form__actions">
          <button type="submit" className="button button--primary" disabled={checking}>
            {checking ? "Conferindo a conta…" : "Revisar transferência"}
          </button>
        </div>
      </form>
    </>
  );
}

interface ReviewProps {
  heading: RefObject<HTMLHeadingElement | null>;
  intent: Intent;
  onBack: () => void;
  onInternalDone: (transfer: TransferView) => void;
  onExternalCreated: (transfer: ExternalTransferView) => void;
}

/**
 * Tela de resumo. Confirmar manda a chave da intenção: clique duplo, reenvio depois de erro de rede ou 503 repetem a
 * mesma chave e a API devolve a mesma resposta, sem segunda transferência (ADR-005).
 */
function Review({ heading, intent, onBack, onInternalDone, onExternalCreated }: ReviewProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [rejected, setRejected] = useState(false);
  const sending = useRef(false);

  async function confirm() {
    if (sending.current) {
      return;
    }

    sending.current = true;
    setBusy(true);
    setError(null);
    try {
      if (intent.destination.kind === "internal") {
        const transfer = await api<TransferView>("/api/v1/transfers", {
          method: "POST",
          idempotencyKey: intent.key,
          body: {
            sourceAccountId: intent.source.id,
            destinationAccountId: intent.destination.account.id,
            amount: intent.amount,
            currency: intent.source.currency,
            description: intent.destination.description || null,
          },
        });
        onInternalDone(transfer);
      } else {
        const transfer = await api<ExternalTransferView>("/api/v1/external-transfers", {
          method: "POST",
          idempotencyKey: intent.key,
          body: {
            sourceAccountId: intent.source.id,
            amount: intent.amount,
            currency: intent.source.currency,
            destination: { bank: intent.destination.bank, branch: intent.destination.branch, account: intent.destination.account },
          },
        });
        onExternalCreated(transfer);
      }
    } catch (failure) {
      // 422 é recusa registrada: repetir com a mesma chave só devolve a mesma recusa. O caminho é corrigir.
      setRejected(failure instanceof ApiError && failure.status === 422);
      setError(messageFor(failure));
      setBusy(false);
      sending.current = false;
    }
  }

  return (
    <>
      <header className="page-header">
        <h1 ref={heading} tabIndex={-1}>
          Confira a transferência
        </h1>
        <p>{intent.destination.kind === "internal" ? "Sai na hora, e não dá para desfazer pelo app." : "Vai para o outro banco; você acompanha o andamento na próxima tela."}</p>
      </header>
      <div className="panel">
        <Facts intent={intent} />
        {error && (
          <div className="state state--error" role="alert">
            <p>
              <strong>{rejected ? "Transferência recusada." : "Não foi possível enviar."}</strong> {error}
            </p>
            {!rejected && <p>Pode tentar de novo: a transferência não será feita duas vezes.</p>}
          </div>
        )}
        <div className="form__actions">
          {!rejected && (
            <button type="button" className="button button--primary" onClick={() => void confirm()} aria-disabled={busy}>
              {busy ? "Enviando…" : error ? "Tentar de novo" : "Confirmar transferência"}
            </button>
          )}
          <button type="button" className="button" onClick={onBack} disabled={busy}>
            Corrigir dados
          </button>
        </div>
      </div>
    </>
  );
}

function Facts({ intent }: { intent: Intent }) {
  const facts: [string, ReactNode][] = [
    ["Valor", <strong className="facts__amount">{`${currencySymbol(intent.source.currency)} ${formatAmount(intent.amount)}`}</strong>],
    ["De", accountLabel(intent.source)],
  ];
  if (intent.destination.kind === "internal") {
    facts.push(["Para", `${intent.destination.account.holderName}, ${accountLabel(intent.destination.account)}`]);
    if (intent.destination.description) {
      facts.push(["Descrição", intent.destination.description]);
    }
  } else {
    facts.push(["Para", `Banco ${intent.destination.bank}, agência ${intent.destination.branch}, conta ${intent.destination.account}`]);
  }

  return (
    <dl className="facts">
      {facts.map(([label, value]) => (
        <Fragment key={label}>
          <dt>{label}</dt>
          <dd>{value}</dd>
        </Fragment>
      ))}
    </dl>
  );
}

interface FieldProps {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string;
  hint?: string;
  placeholder?: string;
  inputMode?: "numeric" | "decimal";
  size?: "short";
}

function Field({ id, label, value, onChange, error, hint, placeholder, inputMode, size }: FieldProps) {
  const described = [hint && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(" ") || undefined;
  return (
    <div className={size === "short" ? "field field--short" : "field"}>
      <label htmlFor={id}>{label}</label>
      <input
        id={id}
        type="text"
        inputMode={inputMode}
        autoComplete="off"
        placeholder={placeholder}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        aria-invalid={error ? true : undefined}
        aria-describedby={described}
      />
      {hint && (
        <p id={`${id}-hint`} className="field__hint">
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-error`} className="field__error">
          {error}
        </p>
      )}
    </div>
  );
}
