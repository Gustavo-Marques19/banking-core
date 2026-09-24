import type { ReactNode } from "react";

export function Loading({ what }: { what: string }) {
  return (
    <div className="state" role="status">
      Carregando {what}…
    </div>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return <div className="state">{children}</div>;
}

export function ErrorState({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="state state--error" role="alert">
      <p>
        <strong>Não deu para carregar.</strong> {message}
      </p>
      <button type="button" className="button" onClick={onRetry}>
        Tentar de novo
      </button>
    </div>
  );
}
