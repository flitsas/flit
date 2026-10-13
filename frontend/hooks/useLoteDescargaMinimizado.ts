'use client';

import { useCallback, useState } from 'react';

/**
 * Aviso de la descarga masiva minimizable (extiende HU #13382) — estado de UI «minimizado» del aviso,
 * recordado por lote (AC4).
 *
 * <p>Una sola clave en `localStorage` guarda el id del lote minimizado: un lote nuevo no coincide y
 * aparece abierto, y no se acumulan claves por lote. Todo acceso va en try/catch (modo privado,
 * cuota, política del navegador): si la lectura falla, el aviso arranca abierto; si la escritura
 * falla, el estado sigue en memoria. No toca el seguimiento (`useConsolidadoLoteActual`).</p>
 *
 * Uso de ejemplo:
 *   const [minimizado, setMinimizado] = useLoteDescargaMinimizado(lote?.id ?? null);
 *   setMinimizado(true); // pestaña al borde derecho; se recuerda para ese lote
 */

export const CLAVE_LOTE_MINIMIZADO = 'flit.lote-descarga.minimizado';

function leer(loteId: string): boolean {
  try {
    return window.localStorage.getItem(CLAVE_LOTE_MINIMIZADO) === loteId;
  } catch {
    return false;
  }
}

function escribir(loteId: string, minimizado: boolean): void {
  try {
    if (minimizado) window.localStorage.setItem(CLAVE_LOTE_MINIMIZADO, loteId);
    else if (window.localStorage.getItem(CLAVE_LOTE_MINIMIZADO) === loteId) {
      window.localStorage.removeItem(CLAVE_LOTE_MINIMIZADO);
    }
  } catch {
    /* sin storage: el estado queda solo en memoria */
  }
}

export function useLoteDescargaMinimizado(loteId: string | null): [boolean, (minimizado: boolean) => void] {
  const [estado, setEstado] = useState<{ id: string | null; minimizado: boolean }>(() => ({
    id: loteId,
    minimizado: loteId ? leer(loteId) : false,
  }));

  // Lote distinto (nuevo o recuperado tras recarga): se relee en el mismo render, sin parpadeo
  // «abierta → minimizada» ni transición de salida al montar.
  let actual = estado;
  if (estado.id !== loteId) {
    actual = { id: loteId, minimizado: loteId ? leer(loteId) : false };
    setEstado(actual);
  }

  const fijar = useCallback(
    (minimizado: boolean) => {
      if (!loteId) return;
      escribir(loteId, minimizado);
      setEstado({ id: loteId, minimizado });
    },
    [loteId],
  );

  return [actual.minimizado, fijar];
}
