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
  /**
   * Orden COMPLETO de las columnas (visibles y ocultas). Una columna oculta conserva su lugar, así
   * que al volver a mostrarla reaparece donde estaba.
   */
  order: string[];
  /** Cambia el orden completo; `visible` se recalcula siguiendo ese orden. Optimista, con aviso si falla. */
  setOrder: (next: string[]) => void;
  /**
   * Restablece la vista de esta tabla al default (columnas visibles + orden). Descarta además
   * `widths` (anchos por columna) del valor guardado: otra HU los agrega y deben volver a "Por defecto".
   */
  reset: () => void;
  /** Anchos por columna (clave → px) guardados por el usuario; vacío = todas con su ancho por defecto. */
  widths: Record<string, number>;
  /** Fija el ancho (px) de una columna y lo guarda. Optimista; revierte con aviso si falla. */
  setWidth: (key: string, px: number) => void;
  /** Quita el ancho guardado de una columna (vuelve a su ancho por defecto) y lo guarda. */
  resetWidth: (key: string) => void;
}

/** Solo pares clave → número finito > 0; cualquier otra cosa del valor guardado se descarta. */
function sanitizeWidths(raw: unknown): Record<string, number> {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return {};
  const out: Record<string, number> = {};
  for (const [k, v] of Object.entries(raw as Record<string, unknown>)) {
    if (typeof v === 'number' && Number.isFinite(v) && v > 0) out[k] = Math.round(v);
  }
  return out;
}

/**
 * Evento que el hook emite cuando no se pudo guardar; `ToastProvider` lo escucha y muestra el aviso.
 * Así el aviso reutiliza el toast del proyecto sin acoplar el hook (ni sus tests) al contexto.
 */
export const UI_PREFERENCES_SAVE_ERROR_EVENT = 'flit:ui-preferences-save-error';
export const SAVE_ERROR_MESSAGE = 'No se pudo guardar la vista de columnas. Se mantiene la anterior.';

/** Inserta en `order` las claves del catálogo que faltan, cada una tras su vecina previa del catálogo. */
function mergeOrder(saved: readonly string[], catalog: readonly string[] | undefined): string[] {
  if (!catalog) return [...saved];
  const result = saved.filter((k, i) => catalog.includes(k) && saved.indexOf(k) === i);
  catalog.forEach((key, i) => {
    if (result.includes(key)) return;
    let at = 0;
    for (let j = i - 1; j >= 0; j--) {
      const prev = result.indexOf(catalog[j]);
      if (prev >= 0) {
        at = prev + 1;
        break;
      }
    }
    result.splice(at, 0, key);
  });
  return result;
}

const sameList = (a: readonly string[], b: readonly string[]) =>
  a.length === b.length && a.every((k, i) => k === b[i]);

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
  const [order, setOrderState] = useState<string[]>(() => [...(catalog ?? defaultVisible)]);
  const orderRef = useRef(order);
  // Resto del valor guardado que este hook no gestiona (p. ej. `widths`): se conserva al guardar.
  const extrasRef = useRef<Record<string, unknown>>({});
  const [widths, setWidthsState] = useState<Record<string, number>>({});
  const widthsRef = useRef(widths);
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
    orderRef.current = order;
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
        const extras: Record<string, unknown> = { ...(res?.value ?? {}) };
        for (const k of ['visible', 'known', 'reshown', 'order', 'widths']) delete extras[k];
        extrasRef.current = extras;
        const cleanWidths = sanitizeWidths(res?.value?.widths);
        widthsRef.current = cleanWidths;
        setWidthsState(cleanWidths);
        const savedOrder = res?.value?.order;
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
          // Orden guardado por el usuario; sin él, el canónico del catálogo. Las columnas nuevas
          // del catálogo entran en su posición del catálogo (`mergeOrder`).
          const base =
            Array.isArray(savedOrder) && savedOrder.every((k) => typeof k === 'string')
              ? (savedOrder as string[])
              : (catalogoActual ?? merged);
          const fullOrder = mergeOrder(base, catalogoActual);
          const shown = fullOrder.filter((k) => merged.includes(k));
          // Claves visibles que el orden no conoce (sin catálogo): al final, sin perderlas.
          const extra = merged.filter((k) => !fullOrder.includes(k));
          orderRef.current = [...fullOrder, ...extra];
          setOrderState(orderRef.current);
          setVisibleState([...shown, ...extra]);
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

  const persist = useCallback(
    (
      nextVisible: string[],
      nextOrder: string[],
      opts: { resetting?: boolean; widths?: Record<string, number> } = {},
    ) => {
      const previousVisible = visibleRef.current;
      const previousOrder = orderRef.current;
      const previousWidths = widthsRef.current;
      const nextWidths = opts.resetting ? {} : (opts.widths ?? previousWidths);
      setVisibleState(nextVisible);
      setOrderState(nextOrder);
      setWidthsState(nextWidths);
      visibleRef.current = nextVisible;
      orderRef.current = nextOrder;
      widthsRef.current = nextWidths;
      setSaving(true);
      // try/catch dentro de una función async: atrapa también un `throw` síncrono del cliente.
      (async () => {
        try {
          // `known` deja constancia del catálogo vigente al guardar: es lo que permite que una
          // columna añadida MÁS TARDE se distinga de una que el usuario ocultó a propósito.
          const catalogoActual = catalogRef.current;
          const reshowActual = reshowRef.current;
          // `reshown` registra que la re-muestra única ya se aplicó.
          const reshown = reshowActual?.length
            ? [...new Set([...reshownSavedRef.current, ...reshowActual])]
            : null;
          const baseOrder = catalogoActual ?? defaultsRef.current;
          // El orden solo se persiste si el usuario lo movió; así, sin reordenar, el valor guardado
          // sigue siendo el de siempre y cualquier columna nueva cae en su lugar del catálogo.
          const guardarOrden = !sameList(nextOrder, baseOrder);
          // "Por defecto" también descarta `widths`: la vista vuelve a empezar de cero.
          const extras = opts.resetting ? {} : extrasRef.current;
          await uiPreferencesClient.put(scope, {
            ...extras,
            ...(Object.keys(nextWidths).length > 0 ? { widths: nextWidths } : {}),
            visible: nextVisible,
            ...(guardarOrden ? { order: nextOrder } : {}),
            ...(catalogoActual ? { known: [...catalogoActual] } : {}),
            ...(reshown ? { reshown } : {}),
          });
          if (opts.resetting) extrasRef.current = {};
        } catch {
          // Fallo al guardar: se conserva lo que se veía (nunca tabla en blanco) y se avisa.
          setVisibleState(previousVisible);
          setOrderState(previousOrder);
          visibleRef.current = previousVisible;
          orderRef.current = previousOrder;
          setWidthsState(previousWidths);
          widthsRef.current = previousWidths;
          if (typeof window !== 'undefined') {
            window.dispatchEvent(
              new CustomEvent(UI_PREFERENCES_SAVE_ERROR_EVENT, { detail: SAVE_ERROR_MESSAGE }),
            );
          }
        } finally {
          setSaving(false);
        }
      })();
    },
    [scope],
  );

  const setVisible = useCallback(
    (next: string[]) => {
      // Defensa adicional (el selector ya lo impide en la UI): nunca cero columnas visibles.
      if (next.length === 0) return;
      const faltan = next.filter((k) => !orderRef.current.includes(k));
      persist(next, [...orderRef.current, ...faltan]);
    },
    [persist],
  );

  const setOrder = useCallback(
    (requested: string[]) => {
      // Claves del orden actual que el caller no mencionó (p. ej. no seleccionables) no se pierden.
      const next = [...requested, ...orderRef.current.filter((k) => !requested.includes(k))];
      const nextVisible = next.filter((k) => visibleRef.current.includes(k));
      if (nextVisible.length === 0) return;
      persist(nextVisible, next);
    },
    [persist],
  );

  const reset = useCallback(() => {
    const defaults = [...defaultsRef.current];
    const baseOrder = [...(catalogRef.current ?? defaults)];
    persist(
      baseOrder.filter((k) => defaults.includes(k)),
      baseOrder,
      { resetting: true },
    );
  }, [persist]);

  const setWidth = useCallback(
    (key: string, px: number) => {
      if (!Number.isFinite(px) || px <= 0) return;
      persist(visibleRef.current, orderRef.current, {
        widths: { ...widthsRef.current, [key]: Math.round(px) },
      });
    },
    [persist],
  );

  const resetWidth = useCallback(
    (key: string) => {
      if (!(key in widthsRef.current)) return;
      const next = { ...widthsRef.current };
      delete next[key];
      persist(visibleRef.current, orderRef.current, { widths: next });
    },
    [persist],
  );

  return { visible, status, saving, setVisible, order, setOrder, reset, widths, setWidth, resetWidth };
}
