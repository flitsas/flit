'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { uiPreferencesClient, type UiPreferenceScope } from '@/lib/api/ui-preferences';

export type UiPreferencesStatus = 'loading' | 'ready' | 'error';

export interface UseUiPreferencesResult {
  /** Columnas visibles: la preferencia guardada, o `defaultVisible` mientras carga / si falla. */
  visible: string[];
  /** Informativo únicamente — NUNCA debe usarse para bloquear el render de la tabla. */
  status: UiPreferencesStatus;
  /** `true` mientras se persiste un cambio (para deshabilitar el selector, no la tabla). */
  saving: boolean;
  /** Cambia la selección de columnas; optimista, revierte si el backend falla al guardar. */
  setVisible: (next: string[]) => void;
}

/**
 * Persiste qué columnas ve el usuario en una tabla de trámites (scope `tramites.columns` o
 * `ot.procedures.columns`). Degrada con elegancia (regla dura): un fallo de red al cargar o
 * guardar NUNCA deja la tabla en blanco ni con un spinner infinito — `visible` arranca (y cae)
 * en `defaultVisible`, y el usuario sigue trabajando con las columnas de siempre. El error solo
 * se expone en `status`/`saving` para un aviso discreto y opcional del caller.
 */
export function useUiPreferences(
  scope: UiPreferenceScope,
  defaultVisible: readonly string[],
): UseUiPreferencesResult {
  const [visible, setVisibleState] = useState<string[]>(() => [...defaultVisible]);
  const [status, setStatus] = useState<UiPreferencesStatus>('loading');
  const [saving, setSaving] = useState(false);
  // Ref viva de la selección actual: `setVisible` la usa para revertir ante un fallo de guardado
  // sin tener que recrear el callback cada vez que cambia `visible`. Se sincroniza en un efecto
  // (no durante el render): mutar un ref mientras se renderiza no está permitido.
  const visibleRef = useRef(visible);
  useEffect(() => {
    visibleRef.current = visible;
  });

  useEffect(() => {
    // Carga inicial de la preferencia, envuelta en try/catch DENTRO de una función async: el
    // cliente HTTP normalmente rechaza la promesa ante un fallo (red/4xx/5xx), pero un `throw`
    // SÍNCRONO (p. ej. un mock de pruebas que reemplaza el módulo sin `request`/`tenantHeader`, o
    // cualquier otro fallo inesperado al construir la petición) tumbaría el efecto entero si solo
    // se encadenara `.then().catch()` sobre la llamada — el `.catch()` nunca llegaría a adjuntarse.
    // Con `await` dentro de un `try`, CUALQUIER fallo (síncrono o asíncrono) cae en el `catch` y
    // la tabla sigue con `defaultVisible`: la degradación elegante no puede depender de que el
    // cliente HTTP se comporte "bien".
    let active = true;
    async function load() {
      try {
        const res = await uiPreferencesClient.get(scope);
        if (!active) return;
        const saved = res?.value?.visible;
        // Sin preferencia guardada, el backend responde `value: {}` (AC del contrato) — se
        // conservan las columnas por defecto. Igual si `visible` llega vacío/corrupto.
        if (Array.isArray(saved) && saved.length > 0 && saved.every((k) => typeof k === 'string')) {
          setVisibleState(saved);
        }
        setStatus('ready');
      } catch {
        if (!active) return;
        // Degradación obligatoria: se queda con `defaultVisible`; el error es informativo.
        setStatus('error');
      }
    }
    void load();
    return () => {
      active = false;
    };
  }, [scope]);

  const setVisible = useCallback(
    (next: string[]) => {
      // Defensa adicional (el selector ya lo impide en la UI): nunca cero columnas visibles.
      if (next.length === 0) return;
      const previous = visibleRef.current;
      setVisibleState(next);
      setSaving(true);
      // Mismo motivo que la carga: try/catch dentro de una función async atrapa también un
      // `throw` síncrono del cliente, no solo un rechazo de la promesa.
      (async () => {
        try {
          await uiPreferencesClient.put(scope, { visible: next });
        } catch {
          // Fallo al guardar: revierte a la selección previa sin interrumpir al usuario (puede
          // volver a intentarlo cambiando de nuevo la selección).
          setVisibleState(previous);
        } finally {
          setSaving(false);
        }
      })();
    },
    [scope],
  );

  return { visible, status, saving, setVisible };
}
