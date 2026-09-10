"use client";

import { RefObject, useEffect, useRef, useState } from "react";

/** Constantes del condensado por scroll — GUIA-DOCK §8 / §10. */
const EXPAND_ZONE = 96;
const LOCKOUT_MS = 250;
const UMBRAL_BAJAR = 4;
const UMBRAL_SUBIR = 8;

/**
 * Condensa el dock al bajar por el área de contenido (no `window`: el Shell hace
 * scroll en un contenedor interno). Solo presentación del menú; no toca catálogo
 * ni rutas.
 */
export function useDockScrollCondense(scrollRef: RefObject<HTMLElement | null>) {
  const [condensed, setCondensed] = useState(false);
  const condensedRef = useRef(false);

  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;

    let lastY = el.scrollTop;
    let ticking = false;
    let lockUntil = 0;

    const onScroll = () => {
      if (ticking) return;
      ticking = true;
      requestAnimationFrame(() => {
        const y = el.scrollTop;
        const now = performance.now();

        if (now >= lockUntil) {
          let next: boolean | null = null;
          if (y <= EXPAND_ZONE) next = false;
          else if (y > lastY + UMBRAL_BAJAR) next = true;
          else if (y < lastY - UMBRAL_SUBIR) next = false;

          if (next !== null && next !== condensedRef.current) {
            condensedRef.current = next;
            lockUntil = now + LOCKOUT_MS;
            setCondensed(next);
          }
        }
        lastY = y;
        ticking = false;
      });
    };

    el.addEventListener("scroll", onScroll, { passive: true });
    return () => el.removeEventListener("scroll", onScroll);
  }, [scrollRef]);

  return condensed;
}
