'use client';

import { useCallback, useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { WizardModalidad, WizardState } from '@/lib/api/types/procedure-runtime';

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
  // Sin default hardcodeado: el tenant lo resuelve `tenantHeader` (tenant activo del `?t=` → JWT).
  // Forzar DEV_TENANT_ID aquí mandaba X-Tenant-Id=11111111 y un SuperAdmin veía 404 "not found" en
  // instancias de su compañía real (creadas bajo su tenant del JWT, no bajo el "Flit Dev Tenant").
  tenantId?: string,
  // CF-02 (HU #10883, AC3) — sin instancia todavía (paso 1 antes de crear el trámite): se carga el
  // ESQUELETO de pasos de la modalidad para pintar el wizard. `undefined` deja el hook inerte como
  // siempre. En cuanto llega `instanceId`, manda el wizard real.
  previewModalidad?: WizardModalidad,
) {
  const [state, setState] = useState<WizardHookState>(INITIAL_STATE);

  const refresh = useCallback(async () => {
    if (!instanceId) {
      if (!previewModalidad) return null;
      setState((s) => ({ ...s, loading: true, error: null }));
      try {
        const wizard = await tramitesClient.getWizardPreview(previewModalidad);
        setState((s) => ({ ...s, wizard, loading: false }));
        return wizard;
      } catch (err) {
        setState((s) => ({
          ...s,
          loading: false,
          error: err instanceof Error ? err.message : 'Error al cargar el asistente',
        }));
        return null;
      }
    }
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
  }, [instanceId, tenantId, previewModalidad]);

  // Carga inicial al tener instanceId.
  useEffect(() => {
    void refresh();
  }, [refresh]);

  const clearError = useCallback(() => {
    setState((s) => ({ ...s, error: null }));
  }, []);

  // HU #10549 — si el OT destino deshabilita la validación de identidad, el wizard oculta el paso
  // de identidad (matrícula). El backend ya lo reporta `complete` (no bloquea), así que ocultarlo
  // no afecta el gate; en traspaso la biométrica vive dentro del paso `fur` (nada que ocultar).
  const rawSteps = state.wizard?.steps ?? [];
  const steps =
    state.wizard?.identityValidationEnabled === false
      ? rawSteps.filter((s) => s.key !== 'identidad')
      : rawSteps;

  return {
    state,
    wizard: state.wizard,
    steps,
    canSubmit: state.wizard?.canSubmit ?? false,
    blockers: state.wizard?.blockers ?? [],
    loading: state.loading,
    error: state.error,
    refresh,
    clearError,
  };
}
