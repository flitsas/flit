"use client";

import { useCallback, useEffect, useRef, useState } from "react";

/**
 * HU #10626 — deshabilita temporalmente una acción tras completarse (AC1: reenvío exitoso de
 * invitación) o tras recibir el cooldown real del backend en un 429 (AC2: el usuario intentó
 * reenviar antes de tiempo). `start(seconds)` siempre REEMPLAZA el cooldown activo por el valor
 * indicado — nunca lo extiende sumando — para que el visual refleje el cooldown real informado
 * por el backend (`retryAfterSeconds`) en vez de reiniciar desde un valor fijo del cliente.
 *
 * Genérico y reutilizable: no conoce nada de invitaciones — cualquier botón con un cooldown
 * temporal (visual u obtenido de un header/response 429) puede usarlo.
 */
export interface UseCooldownResult {
  /** `true` mientras quede cooldown activo (el botón debe deshabilitarse). */
  active: boolean;
  /** Segundos restantes, redondeados hacia arriba; `0` cuando no hay cooldown activo. */
  remainingSeconds: number;
  /** Arranca (o reemplaza) el cooldown con la duración indicada, en segundos. `0` o negativo lo cancela. */
  start: (seconds: number) => void;
  /** Cancela el cooldown inmediatamente. */
  clear: () => void;
}

export function useCooldown(): UseCooldownResult {
  const [remainingSeconds, setRemainingSeconds] = useState(0);
  // Se guarda el timestamp objetivo (no solo los segundos) para que el conteo no derive del
  // tick anterior — cada segundo se recalcula contra Date.now(), evitando drift acumulado.
  const endAtRef = useRef<number | null>(null);

  useEffect(() => {
    if (remainingSeconds <= 0) return;
    const interval = setInterval(() => {
      const endAt = endAtRef.current;
      if (!endAt) return;
      const next = Math.max(0, Math.ceil((endAt - Date.now()) / 1000));
      setRemainingSeconds(next);
      if (next <= 0) {
        endAtRef.current = null;
      }
    }, 1000);
    return () => clearInterval(interval);
    // Solo se re-crea el interval al cruzar la frontera activo/inactivo — dentro de un cooldown
    // activo el propio tick recalcula contra endAtRef.current, así que reiniciarlo cada segundo
    // (si `remainingSeconds` estuviera completo en las deps) sería redundante.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [remainingSeconds > 0]);

  const start = useCallback((seconds: number) => {
    const safeSeconds = Math.max(0, Math.ceil(seconds));
    if (safeSeconds <= 0) {
      endAtRef.current = null;
      setRemainingSeconds(0);
      return;
    }
    endAtRef.current = Date.now() + safeSeconds * 1000;
    setRemainingSeconds(safeSeconds);
  }, []);

  const clear = useCallback(() => {
    endAtRef.current = null;
    setRemainingSeconds(0);
  }, []);

  return { active: remainingSeconds > 0, remainingSeconds, start, clear };
}
