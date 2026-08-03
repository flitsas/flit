"use client";

import { useLayoutEffect, useRef, useState } from "react";

const GUTTER = 12;

/** Desplaza el panel en `left` para que no se salga del viewport (GUIA-DOCK §7). */
export function useEdgeClamp<T extends HTMLElement>(open: string | null) {
  const ref = useRef<T>(null);
  const [shift, setShift] = useState(0);

  useLayoutEffect(() => {
    if (!open) {
      setShift(0);
      return;
    }
    const el = ref.current;
    const anchor = el?.offsetParent as HTMLElement | null;
    if (!el || !anchor) return;

    const compute = () => {
      const left = anchor.getBoundingClientRect().left;
      const overflow = left + el.offsetWidth - (window.innerWidth - GUTTER);
      setShift(overflow > 0 ? -overflow : 0);
    };
    compute();
    window.addEventListener("resize", compute);
    return () => window.removeEventListener("resize", compute);
  }, [open]);

  return { ref, shift };
}
