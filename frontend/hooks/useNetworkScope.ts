'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { usePermissions } from '@/hooks/usePermissions';
import { uiPreferencesClient } from '@/lib/api/ui-preferences';
import { fetchCompanyChildren } from '@/lib/api/admin-companies';
import {
  DEFAULT_NETWORK_SCOPE,
  parseNetworkScopePreference,
  type NetworkScopePreference,
} from '@/lib/tramites/network-scope';

/** Un cliente hijo tal como lo ofrece el selector (id + nombre; nada más hace falta). */
export interface NetworkChildOption {
  id: string;
  nombre: string;
}

/**
 * `idle`: no aplica (no es cabeza). `loading`: pidiendo la lista. `ready`: lista disponible.
 * `unavailable`: el endpoint no respondió (p. ej. 403 para un gestor sin permisos admin) — el
 * selector degrada a «Mi compañía | Toda la red» sin lista de hijos, NO a un error.
 */
export type NetworkChildrenStatus = 'idle' | 'loading' | 'ready' | 'unavailable';

export interface UseNetworkScopeResult {
  /** HU #12356 — ¿el usuario es cabeza de grupo (CONCESION | MARCA_BLANCA)? Si no, nada de esto aplica. */
  isGroupParent: boolean;
  /** Alcance vigente. Para quien no es cabeza es SIEMPRE el default (`own`). */
  scope: NetworkScopePreference;
  /** Cambia el alcance: optimista, persiste por usuario y revierte si el guardado falla. */
  setScope: (next: NetworkScopePreference) => void;
  /** `true` cuando el alcance es la red (entera o un hijo): la tabla debe usar las rutas `network/**`. */
  networkActive: boolean;
  /** Hijos de la red, ordenados por nombre. Vacío si no es cabeza o si la lista no está disponible. */
  children: NetworkChildOption[];
  childrenStatus: NetworkChildrenStatus;
  /**
   * `true` cuando ya se sabe con qué alcance arrancar: para quien no es cabeza, de inmediato; para
   * la cabeza, cuando la preferencia respondió (o falló, en cuyo caso manda el default). La tabla
   * espera a esto para no pedir primero «lo propio» y luego «la red» a un usuario que ya la había
   * elegido.
   */
  ready: boolean;
  /** `true` mientras se persiste un cambio (para deshabilitar el selector, no la tabla). */
  saving: boolean;
}

const SCOPE = 'tramites.scope';

/**
 * HU #12363 — alcance de lectura de una cabeza de red (Feature #12257).
 *
 * Encapsula las tres cosas que la tabla (y #12364 en analítica/reportes) necesitan saber y que no
 * deben calcular por su cuenta: si el usuario es cabeza (claim `is_group_parent` del JWT), qué hijos
 * puede elegir y cuál es su alcance guardado. Para un usuario que NO es cabeza no hace NINGUNA
 * llamada: su listado tiene que ser idéntico al de hoy (AC1), también en la red.
 *
 * La preferencia va por usuario al servidor (`/api/v1/me/ui-preferences/tramites.scope`), nunca a
 * localStorage: dos usuarios del mismo cliente comparten navegador con más frecuencia de la que
 * parece y la preferencia de uno no puede afectar al otro (AC5).
 *
 * Deuda: endpoint no-admin de hijas de la cabeza — ver
 * `.claude/state/pending-work-items/2026-09-14-endpoint-hijas-no-admin.md`. Hoy la lista de hijos
 * sale de `GET /api/v1/admin/companies/{head}/children` (contexto admin). Cuando el backend exponga
 * una ruta no-admin para el gestor (p. ej. bajo `/api/v1/tramites/network/children`), cambiar aquí
 * la fuente; mientras tanto un 403 degrada a «Propio | Red» sin lista.
 */
export function useNetworkScope(): UseNetworkScopeResult {
  const { isGroupParent, tenantId, isSuperAdmin } = usePermissions();
  // El SuperAdmin ve todas las compañías por rol, no por jerarquía: para él no hay «mi red».
  const esCabeza = isGroupParent && !isSuperAdmin && !!tenantId;

  const [scope, setScopeState] = useState<NetworkScopePreference>(DEFAULT_NETWORK_SCOPE);
  const [ready, setReady] = useState(!esCabeza);
  const [saving, setSaving] = useState(false);
  const [children, setChildren] = useState<NetworkChildOption[]>([]);
  // Arranca ya en `loading` para la cabeza: el efecto de abajo no tiene que poner estado de forma
  // síncrona (regla `react-hooks/set-state-in-effect`), solo resolverlo cuando llegue la lista.
  const [childrenStatus, setChildrenStatus] = useState<NetworkChildrenStatus>(() =>
    esCabeza ? 'loading' : 'idle',
  );
  const scopeRef = useRef(scope);
  useEffect(() => {
    scopeRef.current = scope;
  });

  // Preferencia guardada — solo para la cabeza. Un fallo (red, 4xx) deja el default: el alcance
  // propio es la degradación aceptable; la inaceptable sería abrir la red sin que el usuario la pida.
  useEffect(() => {
    if (!esCabeza) return;
    let active = true;
    (async () => {
      try {
        const res = await uiPreferencesClient.get(SCOPE);
        if (!active) return;
        setScopeState(parseNetworkScopePreference(res?.value));
      } catch {
        /* default */
      } finally {
        if (active) setReady(true);
      }
    })();
    return () => {
      active = false;
    };
  }, [esCabeza]);

  // Hijos de la red — solo para la cabeza. Fuente admin (ver deuda arriba); si no responde, el
  // selector sigue existiendo con sus dos opciones fijas.
  useEffect(() => {
    if (!esCabeza || !tenantId) return;
    let active = true;
    (async () => {
      try {
        const lista = await fetchCompanyChildren(tenantId);
        if (!active) return;
        setChildren(
          (lista ?? [])
            .map((c) => ({ id: c.id, nombre: c.razonSocial }))
            .sort((a, b) => a.nombre.localeCompare(b.nombre, 'es')),
        );
        setChildrenStatus('ready');
      } catch {
        if (!active) return;
        setChildren([]);
        setChildrenStatus('unavailable');
      }
    })();
    return () => {
      active = false;
    };
  }, [esCabeza, tenantId]);

  const setScope = useCallback(
    (next: NetworkScopePreference) => {
      if (!esCabeza) return;
      const previous = scopeRef.current;
      setScopeState(next);
      setSaving(true);
      (async () => {
        try {
          await uiPreferencesClient.put(SCOPE, {
            mode: next.mode,
            ...(next.childTenantId ? { childTenantId: next.childTenantId } : {}),
          });
        } catch {
          setScopeState(previous);
        } finally {
          setSaving(false);
        }
      })();
    },
    [esCabeza],
  );

  /**
   * AC4 — el selector no ofrece ningún cliente ajeno a la red, y tampoco se FILTRA por uno: si la
   * preferencia trae un hijo que ya no está en la lista (desvinculado), se lee como «toda la red».
   * Con la lista no disponible no se puede comprobar y se respeta lo guardado.
   */
  const scopeEfectivo = useMemo<NetworkScopePreference>(() => {
    if (!esCabeza) return DEFAULT_NETWORK_SCOPE;
    if (
      scope.mode === 'network' &&
      scope.childTenantId &&
      childrenStatus === 'ready' &&
      !children.some((c) => c.id === scope.childTenantId)
    ) {
      return { mode: 'network' };
    }
    return scope;
  }, [esCabeza, scope, children, childrenStatus]);

  return {
    isGroupParent: esCabeza,
    scope: scopeEfectivo,
    setScope,
    networkActive: esCabeza && scopeEfectivo.mode === 'network',
    children,
    childrenStatus,
    ready,
    saving,
  };
}
