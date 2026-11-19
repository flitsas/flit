"use client";

import { useEffect, useState } from "react";

/**
 * Espera antes de escribir en una región viva recién montada (HU #14562 · M11 de la auditoría de la oleada). NVDA, JAWS
 * y VoiceOver no anuncian de forma fiable una región que nace con su texto: primero debe existir vacía en el árbol
 * accesible y después recibir el contenido. 50 ms alcanzan para que el navegador la publique y no se notan en pantalla.
 */
export const REGION_VIVA_RETARDO_MS = 50;

/**
 * Devuelve `false` en el primer render y `true` pasado el retardo. La región se monta vacía mientras sea `false`.
 * Con `activa = false` siempre devuelve `false`, y al volver a `true` espera otra vez.
 */
export function useRegionVivaLista(activa = true, retardoMs = REGION_VIVA_RETARDO_MS): boolean {
  const [lista, setLista] = useState(false);
  useEffect(() => {
    if (!activa) return;
    const t = setTimeout(() => setLista(true), retardoMs);
    return () => {
      clearTimeout(t);
      setLista(false);
    };
  }, [activa, retardoMs]);
  return activa && lista;
}
