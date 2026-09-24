import { createContext, useContext } from "react";
import type { BffUser } from "./api/types";

export interface Session extends BffUser {
  isOperator: boolean;
  isAdmin: boolean;
}

export function toSession(user: BffUser): Session {
  return { ...user, isOperator: user.roles.includes("operator"), isAdmin: user.roles.includes("admin") };
}

export const SessionContext = createContext<Session | null>(null);

export function useSession(): Session {
  const session = useContext(SessionContext);
  if (!session) {
    throw new Error("useSession fora do SessionContext.");
  }

  return session;
}
