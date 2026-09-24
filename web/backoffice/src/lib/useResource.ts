import { useEffect, useRef, useState } from "react";

export type Resource<T> =
  | { state: "loading" }
  | { state: "ready"; data: T }
  | { state: "error"; message: string };

/**
 * Carrega quando `key` muda e expõe `reload`. Resposta de uma carga antiga não sobrescreve a mais nova.
 */
export function useResource<T>(load: () => Promise<T>, key = ""): [Resource<T>, () => void] {
  const [resource, setResource] = useState<Resource<T>>({ state: "loading" });
  const [version, setVersion] = useState(0);
  const latestLoad = useRef(load);
  latestLoad.current = load;

  useEffect(() => {
    let current = true;
    setResource({ state: "loading" });
    latestLoad.current().then(
      (data) => current && setResource({ state: "ready", data }),
      (error: unknown) => current && setResource({ state: "error", message: error instanceof Error ? error.message : String(error) }),
    );
    return () => {
      current = false;
    };
  }, [key, version]);

  return [resource, () => setVersion((v) => v + 1)];
}
