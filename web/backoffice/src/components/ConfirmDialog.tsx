import { Fragment, useEffect, useRef, useState, type ReactNode } from "react";
import { ApiError } from "../api/client";

interface Props {
  title: string;
  facts: [string, ReactNode][];
  confirmLabel: string;
  tone: "primary" | "danger";
  onConfirm: () => Promise<void>;
  onClose: () => void;
}

/**
 * Diálogo nativo: Esc fecha e o foco volta para quem abriu. A decisão só vale depois de ler o resumo.
 */
export function ConfirmDialog({ title, facts, confirmLabel, tone, onConfirm, onClose }: Props) {
  const ref = useRef<HTMLDialogElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const dialog = ref.current;
    dialog?.showModal?.();
    return () => dialog?.close?.();
  }, []);

  async function confirm() {
    setBusy(true);
    setError(null);
    try {
      await onConfirm();
      onClose();
    } catch (failure) {
      setError(failure instanceof ApiError || failure instanceof Error ? failure.message : String(failure));
      setBusy(false);
    }
  }

  return (
    <dialog ref={ref} aria-labelledby="confirm-title" onClose={onClose} onCancel={onClose}>
      <h2 id="confirm-title" className="dialog__title">
        {title}
      </h2>
      <dl className="facts">
        {facts.map(([label, value]) => (
          <Fragment key={label}>
            <dt>{label}</dt>
            <dd>{value}</dd>
          </Fragment>
        ))}
      </dl>
      {error && (
        <p className="field__error dialog__error" role="alert">
          {error}
        </p>
      )}
      <div className="form__actions">
        <button type="button" className={tone === "primary" ? "button button--primary" : "button button--danger"} onClick={confirm} disabled={busy}>
          {busy ? "Enviando…" : confirmLabel}
        </button>
        <button type="button" className="button" onClick={onClose} disabled={busy}>
          Voltar
        </button>
      </div>
    </dialog>
  );
}
