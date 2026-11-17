"use client";

import { useCallback, useState } from "react";

/**
 * Región viva única de una pantalla (spec-ui §1.6, HU #14577): un `<p role="status" aria-live="polite" class="sr-only">`
 * vacío desde el montaje en el que se escriben los resultados de las acciones. Cada anuncio va en un `<span key={n}>`
 * nuevo, así dos textos iguales seguidos se leen las dos veces (patrón de `TablaEditable`). Los errores no van aquí: van
 * en `role="alert"` junto al control que falló.
 *
 *   const { anunciar, region } = useRegionVivaPantalla();
 *   …
 *   <BotonSanear … onAnunciar={anunciar} />
 *   {region}
 */
export function useRegionVivaPantalla(): { anunciar: (texto: string) => void; region: React.ReactNode } {
  const [anuncio, setAnuncio] = useState<{ n: number; texto: string } | null>(null);
  const anunciar = useCallback((texto: string) => setAnuncio((a) => ({ n: (a?.n ?? 0) + 1, texto })), []);
  const region = (
    <p role="status" aria-live="polite" className="sr-only">
      {anuncio && <span key={anuncio.n}>{anuncio.texto}</span>}
    </p>
  );
  return { anunciar, region };
}
