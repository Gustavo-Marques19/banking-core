import { currencySymbol, formatAmount } from "@banking/web-shared/money";

/** Valor com símbolo da moeda. Débito leva sinal de menos escrito: a direção nunca fica só na cor (DESIGN.md). */
export function Money({ value, currency, direction }: { value: string; currency: string; direction?: "debit" | "credit" }) {
  const sign = direction === "debit" ? "−" : direction === "credit" ? "+" : "";
  return (
    <span className={direction ? `money money--${direction}` : "money"}>
      {sign && <span aria-hidden="true">{sign} </span>}
      {direction && <span className="visually-hidden">{direction === "debit" ? "saída de " : "entrada de "}</span>}
      {currencySymbol(currency)} {formatAmount(value)}
    </span>
  );
}
