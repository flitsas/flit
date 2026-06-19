'use client';

import { useCallback, useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { DEV_TENANT_ID } from '@/lib/api/dev-constants';
import type { ProcedureActor } from '@/lib/api/types/procedure-runtime';

export interface ProcedureActorsState {
  actors: ProcedureActor[] | null;
  loading: boolean;
  saving: boolean;
  saved: boolean;
  error: string | null;
}

const INITIAL_STATE: ProcedureActorsState = {
  actors: null,
  loading: false,
  saving: false,
  saved: false,
  error: null,
};

/**
 * Carga/guarda el conjunto de actores de una instancia de trámite.
 * `instanceId` null deja el hook inerte (útil mientras la instancia no existe).
 * Se diseña para extraerse al step de actores del wizard (Slice 4).
 */
export function useProcedureActors(
  instanceId: string | null,
  tenantId: string = DEV_TENANT_ID,
) {
  const [state, setState] = useState<ProcedureActorsState>(INITIAL_STATE);

  const load = useCallback(async () => {
    if (!instanceId) return;
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      const actors = await tramitesClient.getActors(instanceId, tenantId);
      setState((s) => ({ ...s, actors, loading: false }));
    } catch (err) {
      setState((s) => ({
        ...s,
        loading: false,
        error:
          err instanceof Error ? err.message : 'Error al cargar los actores',
      }));
    }
  }, [instanceId, tenantId]);

  // Carga inicial al tener instanceId.
  useEffect(() => {
    void load();
  }, [load]);

  const save = useCallback(
    async (actors: ProcedureActor[]) => {
      if (!instanceId) return false;
      setState((s) => ({ ...s, saving: true, saved: false, error: null }));
      try {
        await tramitesClient.saveActors(instanceId, actors, tenantId);
        setState((s) => ({ ...s, actors, saving: false, saved: true }));
        return true;
      } catch (err) {
        setState((s) => ({
          ...s,
          saving: false,
          error:
            err instanceof Error ? err.message : 'Error al guardar los actores',
        }));
        return false;
      }
    },
    [instanceId, tenantId],
  );

  const clearError = useCallback(() => {
    setState((s) => ({ ...s, error: null }));
  }, []);

  return { state, load, save, clearError };
}
