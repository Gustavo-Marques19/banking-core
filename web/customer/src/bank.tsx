import { createContext, useContext } from "react";
import { useSearchParams } from "react-router";
import type { AccountView, BffUser, CustomerView } from "./api/types";

export interface Bank {
  user: BffUser;
  customer: CustomerView;
  accounts: AccountView[];
}

export const BankContext = createContext<Bank | null>(null);

export function useBank(): Bank {
  const bank = useContext(BankContext);
  if (!bank) {
    throw new Error("useBank fora do BankContext.");
  }

  return bank;
}

/** Conta escolhida em ?conta=, ou a primeira. Id que não é do cliente cai na primeira, sem erro. */
export function useSelectedAccount(): AccountView {
  const { accounts } = useBank();
  const [params] = useSearchParams();
  return accounts.find((account) => account.id === params.get("conta")) ?? accounts[0]!;
}

export function accountLabel(account: { branch: string; number: string }): string {
  return `Agência ${account.branch} · Conta ${account.number}`;
}
