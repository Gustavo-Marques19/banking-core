import { currencySymbol, formatAmount } from "@banking/web-shared/money";

export function Amount({ value, currency }: { value: string; currency: string }) {
  return (
    <span className="request__amount">
      <small>{currencySymbol(currency)}</small>
      {formatAmount(value)}
    </span>
  );
}
