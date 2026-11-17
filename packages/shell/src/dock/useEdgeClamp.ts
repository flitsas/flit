"use client";

import { useLayoutEffect, useRef, useState } from "react";

const GUTTER = 12;

/**
 * Desplaza el panel para que no se salga del viewport (GUIA-DOCK §7). El panel va centrado sobre su píldora, como el
 * submenú del prototipo (HU #14479); el desplazamiento corrige el borde que se salga, a la izquierda o a la derecha.
 */
export function useEdgeClamp<T extends HTMLElement>(open: string | null) {
  const ref = useRef<T>(null);
  const [shift, setShift] = useState(0);

  useLayoutEffect(() => {
    if (!open) return;
    const el = ref.current;
    const anchor = el?.offsetParent as HTMLElement | null;
    if (!el || !anchor) return;

    const compute = () => {
      const box = anchor.getBoundingClientRect();
      const left = box.left + box.width / 2 - el.offsetWidth / 2;
      const overflowRight = left + el.offsetWidth - (window.innerWidth - GUTTER);
      const overflowLeft = GUTTER - left;
      setShift(overflowRight > 0 ? -overflowRight : overflowLeft > 0 ? overflowLeft : 0);
    };
    compute();
    window.addEventListener("resize", compute);
    return () => window.removeEventListener("resize", compute);
  }, [open]);

  // Con el panel cerrado el desplazamiento no aplica: se deriva en el render en vez de
  // resetearlo con un setState dentro del efecto.
  return { ref, shift: open ? shift : 0 };
}
