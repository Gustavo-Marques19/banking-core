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

/**
 * Valor digitado em pt-BR ("1.234,56", "100", "0,5") vira o formato da API ("1234.56"). Devolve null se não for um
 * valor positivo com até 2 casas. Não passa por number, pelo mesmo motivo de formatAmount.
 */
export function parseAmount(input: string): string | null {
  const text = input.replace(/\s/g, "").replace(/^R\$/i, "");
  const match = /^(\d{1,3}(?:\.\d{3})+|\d+)(?:,(\d{1,2}))?$/.exec(text);
  if (!match) {
    return null;
  }

  const integer = (match[1] ?? "0").replaceAll(".", "").replace(/^0+(?=\d)/, "");
  const fraction = (match[2] ?? "").padEnd(2, "0");
  if (/^0+$/.test(integer + fraction)) {
    return null;
  }

  return `${integer}.${fraction}`;
}
