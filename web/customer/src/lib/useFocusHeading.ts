import { useEffect, useRef } from "react";

/**
 * Leva o foco ao título quando a tela ou o passo muda, para o leitor de tela anunciar onde a pessoa está.
 * Na primeira montagem não mexe: quem chegou pela navegação já está no lugar certo.
 */
export function useFocusHeading<T extends HTMLElement>(step: string) {
  const ref = useRef<T>(null);
  const first = useRef(true);

  useEffect(() => {
    if (first.current) {
      first.current = false;
      return;
    }

    ref.current?.focus();
  }, [step]);

  return ref;
}
