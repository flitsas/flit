'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { uiPreferencesClient, type UiPreferenceScope } from '@/lib/api/ui-preferences';

export type UiPreferencesStatus = 'loading' | 'ready' | 'error';

export interface UseUiPreferencesOptions {
  /**
   * Catálogo COMPLETO de columnas conocidas hoy. Se persiste junto a la selección (`known`) para
   * poder distinguir "el usuario ocultó esta columna" de "esta columna no existía cuando guardó".
   * Sin esto, toda columna nueva nace invisible para quien ya tenía preferencia guardada, y desde
   * el selector parece un dato que falta.
   */
  catalog?: readonly string[];
  /**
   * Claves incorporadas al catálogo DESPUÉS de que se empezaran a guardar preferencias, cuando el
   * valor persistido todavía no llevaba `known` y por tanto no se puede deducir qué conocía el
   * usuario. Solo estas se añaden a una preferencia antigua; el resto de su selección se respeta
   * tal cual, porque ocultar una columna que ya existía SÍ fue una decisión suya.
   */
  addedSinceLegacy?: readonly string[];
  /**
   * Claves que YA existían en el catálogo y pasaron al default más tarde: `known` no sirve para
   * ellas (el usuario sí pudo ocultarlas). Se re-muestran UNA sola vez a quien tiene preferencia
   * guardada sin ellas; la marca `reshown` que se persiste al guardar deja constancia de que ya se
   * hizo, y a partir de ahí, si el usuario la oculta, su decisión manda.
   */
  reshowOnce?: readonly string[];
}

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
  options: UseUiPreferencesOptions = {},
): UseUiPreferencesResult {
  const { catalog, addedSinceLegacy, reshowOnce } = options;
  const [visible, setVisibleState] = useState<string[]>(() => [...defaultVisible]);
  const [status, setStatus] = useState<UiPreferencesStatus>('loading');
  const [saving, setSaving] = useState(false);
  // Ref viva de la selección actual: `setVisible` la usa para revertir ante un fallo de guardado
  // sin tener que recrear el callback cada vez que cambia `visible`. Se sincroniza en un efecto
  // (no durante el render): mutar un ref mientras se renderiza no está permitido.
  const visibleRef = useRef(visible);
  // Mismo patrón para el catálogo y el default: son entradas estáticas del caller, pero si alguno
  // las pasara como arreglo inline, incluirlas en las deps del efecto de carga dispararía un GET
  // en cada render. Con refs el efecto sigue dependiendo SOLO de `scope`, que es lo correcto.
  const catalogRef = useRef(catalog);
  const defaultsRef = useRef(defaultVisible);
  const addedRef = useRef(addedSinceLegacy);
  const reshowRef = useRef(reshowOnce);
  // Marca `reshown` tal como llegó del backend: al guardar se une con `reshowOnce` para no perder
  // re-muestras de otras claves que ya se hubieran hecho.
  const reshownSavedRef = useRef<string[]>([]);
  useEffect(() => {
    visibleRef.current = visible;
    catalogRef.current = catalog;
    defaultsRef.current = defaultVisible;
    addedRef.current = addedSinceLegacy;
    reshowRef.current = reshowOnce;
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
        const rawKnown = res?.value?.known;
        const rawReshown = res?.value?.reshown;
        const reshown =
          Array.isArray(rawReshown) && rawReshown.every((k) => typeof k === 'string')
            ? (rawReshown as string[])
            : [];
        reshownSavedRef.current = reshown;
        // Sin preferencia guardada, el backend responde `value: {}` (AC del contrato) — se
        // conservan las columnas por defecto. Igual si `visible` llega vacío/corrupto.
        if (Array.isArray(saved) && saved.length > 0 && saved.every((k) => typeof k === 'string')) {
          const known =
            Array.isArray(rawKnown) && rawKnown.every((k) => typeof k === 'string')
              ? rawKnown
              : null;
          // Columnas que el usuario NO pudo haber decidido porque no existían al guardar: entran
          // con su visibilidad por defecto. Con `known` se deduce con exactitud; sin él (formato
          // antiguo) solo se añaden las declaradas en `addedSinceLegacy` — nunca se re-muestra
          // una columna que el usuario sí pudo haber ocultado a conciencia.
          const catalogoActual = catalogRef.current;
          const defaults = defaultsRef.current;
          const nuevas = known
            ? (catalogoActual ?? []).filter((k) => !known.includes(k) && defaults.includes(k))
            : (addedRef.current ?? []).filter((k) => defaults.includes(k));
          // Re-muestra única: solo si la marca persistida aún no la registra.
          const reMostrar = (reshowRef.current ?? []).filter(
            (k) => defaults.includes(k) && !reshown.includes(k),
          );
          const merged = [
            ...saved,
            ...[...nuevas, ...reMostrar].filter((k, i, all) => !saved.includes(k) && all.indexOf(k) === i),
          ];
          // Orden canónico del catálogo, no el de llegada.
          setVisibleState(
            catalogoActual ? catalogoActual.filter((k) => merged.includes(k)) : merged,
          );
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
          // `known` deja constancia del catálogo vigente al guardar: es lo que permite que una
          // columna añadida MÁS TARDE se distinga de una que el usuario ocultó a propósito.
          const catalogoActual = catalogRef.current;
          const reshowActual = reshowRef.current;
          // `reshown` registra que la re-muestra única ya se aplicó: desde aquí, si el usuario oculta
          // la columna, la próxima carga lo respeta.
          const reshown = reshowActual?.length
            ? [...new Set([...reshownSavedRef.current, ...reshowActual])]
            : null;
          await uiPreferencesClient.put(scope, {
            visible: next,
            ...(catalogoActual ? { known: [...catalogoActual] } : {}),
            ...(reshown ? { reshown } : {}),
          });
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
