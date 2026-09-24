import { useState } from "react";

/**
 * Código da operação, o mesmo que a auditoria do banco usa. É o que o cliente passa ao suporte, então fica inteiro
 * na tela e com um botão de copiar.
 */
export function OperationCode({ code }: { code: string }) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(code);
      setCopied(true);
    } catch {
      // Sem permissão de área de transferência: o código continua selecionável na tela.
      setCopied(false);
    }
  }

  return (
    <span className="code">
      <span className="code__value">{code}</span>
      <button type="button" className="button" onClick={() => void copy()}>
        {copied ? "Copiado" : "Copiar"}
      </button>
      <span className="visually-hidden" aria-live="polite">
        {copied ? "Código copiado." : ""}
      </span>
    </span>
  );
}
