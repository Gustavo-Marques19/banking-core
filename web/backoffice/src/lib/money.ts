/**
 * "12345.60" vira "12.345,60". Trabalha só com a string: dinheiro nunca passa por number (ADR-002).
 */
export function formatAmount(amount: string): string {
  const match = /^(-?)(\d+)(?:\.(\d+))?$/.exec(amount);
  if (!match) {
    return amount;
  }

  const [, sign, integer = "0", fraction = ""] = match;
  const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, ".");
  return `${sign}${grouped}${fraction ? `,${fraction}` : ""}`;
}

const symbols: Record<string, string> = { BRL: "R$" };

export function currencySymbol(currency: string): string {
  return symbols[currency] ?? currency;
}

const dateTime = new Intl.DateTimeFormat("pt-BR", { dateStyle: "short", timeStyle: "short", timeZone: "America/Sao_Paulo" });

export function formatDateTime(iso: string): string {
  return dateTime.format(new Date(iso));
}

/** Id longo encurtado para leitura; o valor inteiro fica no title. */
export function shortId(id: string): string {
  return id.length > 12 ? `${id.slice(0, 8)}…` : id;
}
