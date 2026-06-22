'use client';

import { useCallback, useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { DEV_TENANT_ID } from '@/lib/api/dev-constants';
import type { WizardState } from '@/lib/api/types/procedure-runtime';

export interface WizardHookState {
  wizard: WizardState | null;
  loading: boolean;
  error: string | null;
}

const INITIAL_STATE: WizardHookState = {
  wizard: null,
  loading: false,
  error: null,
};

/**
 * Carga el estado server-driven del wizard (GET /wizard) y expone un
 * `refresh()` que la shell re-consulta tras cada acción que pueda mover gates
 * (guardar actor, subir documento, correr preflight, guardar comercial). El
 * backend manda steps/status/canSubmit/blockers; el cliente nunca los recalcula.
 *
 * `instanceId` null deja el hook inerte (la instancia draft aún no existe).
 */
export function useWizard(
  instanceId: string | null,
  tenantId: string = DEV_TENANT_ID,
) {
  const [state, setState] = useState<WizardHookState>(INITIAL_STATE);

  const refresh = useCallback(async () => {
    if (!instanceId) return null;
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      const wizard = await tramitesClient.getWizardState(instanceId, tenantId);
      setState((s) => ({ ...s, wizard, loading: false }));
      return wizard;
    } catch (err) {
      setState((s) => ({
        ...s,
        loading: false,
        error:
          err instanceof Error ? err.message : 'Error al cargar el asistente',
      }));
      return null;
    }
  }, [instanceId, tenantId]);

  // Carga inicial al tener instanceId.
  useEffect(() => {
    void refresh();
  }, [refresh]);

  const clearError = useCallback(() => {
    setState((s) => ({ ...s, error: null }));
  }, []);

  return {
    state,
    wizard: state.wizard,
    steps: state.wizard?.steps ?? [],
    canSubmit: state.wizard?.canSubmit ?? false,
    blockers: state.wizard?.blockers ?? [],
    loading: state.loading,
    error: state.error,
    refresh,
    clearError,
  };
}
