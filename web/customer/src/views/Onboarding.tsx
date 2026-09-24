import { api } from "@banking/web-shared/client";
import { useState, type FormEvent } from "react";
import type { AccountView, CustomerView } from "../api/types";
import { messageFor } from "../lib/messages";

interface Props {
  customer: CustomerView | null;
  onDone: () => void;
}

/** Cadastro e abertura de conta. Quem já se cadastrou e não tem conta cai direto no segundo passo. */
export function Onboarding({ customer, onDone }: Props) {
  const [name, setName] = useState("");
  const [document, setDocument] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  // Cadastro feito e conta ainda não: tentar de novo só abre a conta, sem cadastrar outra vez.
  const [registered, setRegistered] = useState(customer !== null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (!registered) {
      if (name.trim().split(/\s+/).length < 2) {
        setError("Informe nome e sobrenome.");
        return;
      }

      if (document.replace(/\D/g, "").length !== 11) {
        setError("O CPF tem 11 números.");
        return;
      }
    }

    setBusy(true);
    try {
      if (!registered) {
        await api<CustomerView>("/api/v1/customers", { method: "POST", body: { name: name.trim(), document } });
        setRegistered(true);
      }

      await api<AccountView>("/api/v1/accounts", { method: "POST", body: { currency: "BRL" } });
      onDone();
    } catch (failure) {
      setError(messageFor(failure));
      setBusy(false);
    }
  }

  return (
    <>
      <header className="page-header">
        <h1>{customer ? "Falta abrir a sua conta" : "Abra sua conta"}</h1>
        <p>
          {customer
            ? `Cadastro de ${customer.name} (CPF ${customer.document}) feito. A conta corrente em reais sai na hora.`
            : "Nome e CPF, e a conta corrente em reais sai na hora."}
        </p>
      </header>

      <form className="form panel" onSubmit={submit} noValidate>
        {!customer && (
          <>
            <p className="notice">
              Banco de demonstração, com dinheiro fictício. Não use o seu CPF real: um CPF gerado para teste funciona.
            </p>
            <div className="field">
              <label htmlFor="name">Nome completo</label>
              <input id="name" type="text" autoComplete="name" value={name} onChange={(e) => setName(e.target.value)} required />
            </div>
            <div className="field">
              <label htmlFor="document">CPF</label>
              <input
                id="document"
                type="text"
                inputMode="numeric"
                autoComplete="off"
                placeholder="000.000.000-00"
                aria-describedby="document-hint"
                value={document}
                onChange={(e) => setDocument(e.target.value)}
                required
              />
              <p id="document-hint" className="field__hint">
                Fica guardado criptografado. Nas telas aparece só parte dele.
              </p>
            </div>
          </>
        )}
        {error && (
          <p className="field__error" role="alert">
            {error}
          </p>
        )}
        <div className="form__actions">
          <button type="submit" className="button button--primary" disabled={busy}>
            {busy ? "Abrindo…" : "Abrir conta"}
          </button>
        </div>
      </form>
    </>
  );
}
