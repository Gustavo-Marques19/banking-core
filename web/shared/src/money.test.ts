import { describe, expect, it } from "vitest";
import { formatAmount, parseAmount } from "./money";

describe("formatAmount", () => {
  it.each([
    ["0.00", "0,00"],
    ["10.50", "10,50"],
    ["10000.01", "10.000,01"],
    ["1234567.89", "1.234.567,89"],
    ["-5.10", "-5,10"],
  ])("%s vira %s sem passar por number", (input, expected) => {
    expect(formatAmount(input)).toBe(expected);
  });

  it("não perde precisão em valores que um double arredondaria", () => {
    expect(formatAmount("999999999999999.99")).toBe("999.999.999.999.999,99");
  });
});

describe("parseAmount", () => {
  it.each([
    ["100", "100.00"],
    ["100,5", "100.50"],
    ["1.234,56", "1234.56"],
    ["R$ 1.234.567,89", "1234567.89"],
    ["0,01", "0.01"],
    ["007,10", "7.10"],
  ])("%s vira %s", (input, expected) => {
    expect(parseAmount(input)).toBe(expected);
  });

  it.each(["", "0", "0,00", "-5", "1,234", "1.23", "12.34,5", "abc", "1e3", "10.5"])("recusa %s", (input) => {
    expect(parseAmount(input)).toBeNull();
  });
});
