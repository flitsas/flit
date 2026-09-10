'use client';

import { useCallback, useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { useRevalidateOnFocus } from './useRevalidateOnFocus';
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
  // Sin default hardcodeado: el tenant lo resuelve `tenantHeader` (tenant activo del `?t=` → JWT).
  // Forzar DEV_TENANT_ID aquí mandaba X-Tenant-Id=11111111 y un SuperAdmin veía 404 "not found" en
  // instancias de su compañía real (creadas bajo su tenant del JWT, no bajo el "Flit Dev Tenant").
  tenantId?: string,
  // CF-02 (HU #10883, AC3) — sin instancia todavía (paso 1 antes de crear el trámite): se carga el
  // ESQUELETO de pasos del TIPO para pintar el wizard. `undefined` deja el hook inerte como siempre.
  // En cuanto llega `instanceId`, manda el wizard real.
  //
  // ADR-0050 — antes se pedía por modalidad y solo existía para matrícula y traspaso; ahora es el
  // `code` del tipo, así que el esqueleto existe para cualquier tipo parametrizado.
  previewProcedureTypeCode?: string,
) {
  const [state, setState] = useState<WizardHookState>(INITIAL_STATE);

  /**
   * Relee el estado del asistente (pasos, bloqueos, `canSubmit`).
   *
   * En segundo plano (`background`) no toca `loading` ni `error`, ni borra el estado que ya está en
   * pantalla si la llamada falla: se dispara sola al recuperar el foco y no debe interrumpir a nadie.
   */
  const refresh = useCallback(
    async (opts?: { background?: boolean }) => {
      const silencioso = opts?.background === true;
      if (!instanceId) {
        if (!previewProcedureTypeCode) return null;
        if (!silencioso) setState((s) => ({ ...s, loading: true, error: null }));
        try {
          const wizard = await tramitesClient.getWizardPreview(previewProcedureTypeCode);
          setState((s) => ({ ...s, wizard, loading: false }));
          return wizard;
        } catch (err) {
          if (silencioso) return null;
          setState((s) => ({
            ...s,
            loading: false,
            error: err instanceof Error ? err.message : 'Error al cargar el asistente',
          }));
          return null;
        }
      }
      if (!silencioso) setState((s) => ({ ...s, loading: true, error: null }));
      try {
        const wizard = await tramitesClient.getWizardState(instanceId, tenantId);
        setState((s) => ({ ...s, wizard, loading: false }));
        return wizard;
      } catch (err) {
        if (silencioso) return null;
        setState((s) => ({
          ...s,
          loading: false,
          error:
            err instanceof Error ? err.message : 'Error al cargar el asistente',
        }));
        return null;
      }
    },
    [instanceId, tenantId, previewProcedureTypeCode],
  );

  // Carga inicial al tener instanceId.
  useEffect(() => {
    void refresh();
  }, [refresh]);

  // El checklist no es el único que se quedaba viejo: un documento obligatorio dado de alta con la
  // pantalla abierta tampoco aparecía entre los bloqueos, así que el paso dejaba pasar. Reponer el
  // paso activo no es un riesgo aquí: la colocación inicial va guardada por un ref en TramiteWizard
  // y el efecto correctivo solo actúa si el paso dejó de ser alcanzable por la cascada.
  const revalidarEnFoco = useCallback(() => {
    void refresh({ background: true });
  }, [refresh]);
  useRevalidateOnFocus(revalidarEnFoco, Boolean(instanceId));

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
