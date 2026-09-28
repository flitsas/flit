"use client";

import { RefObject, useEffect, useRef, useState } from "react";

/** Constantes del condensado por scroll — GUIA-DOCK §8 / §10. */
const EXPAND_ZONE = 96;
const LOCKOUT_MS = 250;
const UMBRAL_BAJAR = 4;
const UMBRAL_SUBIR = 8;

/**
 * Condensa el dock al bajar y lo expande al subir o cerca del inicio. Escucha el contenedor con scroll del layout `app`
 * (Trámites) o, sin él, la ventana (hub). Solo presentación del menú; no toca catálogo ni rutas. Viene del Shell de
 * Trámites (B-13).
 */
export function useDockScrollCondense(scrollRef?: RefObject<HTMLElement | null>) {
  const [condensed, setCondensed] = useState(false);
  const condensedRef = useRef(false);

  useEffect(() => {
    const box = scrollRef ? scrollRef.current : null;
    if (scrollRef && !box) return;
    const readY = () => (box ? box.scrollTop : window.scrollY);
    const target: HTMLElement | Window = box ?? window;

    let lastY = readY();
    let ticking = false;
    let lockUntil = 0;

    const onScroll = () => {
      if (ticking) return;
      ticking = true;
      requestAnimationFrame(() => {
        const y = readY();
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

    target.addEventListener("scroll", onScroll, { passive: true });
    return () => target.removeEventListener("scroll", onScroll);
  }, [scrollRef]);

  return condensed;
}
