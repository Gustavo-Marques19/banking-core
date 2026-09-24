import { describe, expect, it } from "vitest";
import { formatAmount } from "../lib/money";

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
