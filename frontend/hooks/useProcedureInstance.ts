'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { getToken } from '@/lib/api/client';
import { decodeJwtPayload } from '@/lib/auth/jwt';
import { DEV_TENANT_ID, DEV_USER_ID } from '@/lib/api/dev-constants';
import type { FormFieldItem } from '@/lib/api/types/procedure-parametrization';
import type {
  CreateInstanceRequest,
  FieldValueInput,
  PreflightSnapshot,
  ProcedureInstanceDetail,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

/**
 * Decide en qué columna del valor partido guardar según el fieldType:
 * - text/textarea/select/radio/number/date → valueText (texto plano)
 * - checkbox y estructuras → valueJson (JSON string)
 */
export function buildFieldValueInput(
  field: Pick<FormFieldItem, 'id' | 'fieldKey' | 'fieldType'>,
  rawValue: unknown,
): FieldValueInput {
  const formFieldId = field.id ?? null;
  if (field.fieldType === 'checkbox') {
    return {
      formFieldId,
      fieldKey: field.fieldKey,
      valueJson: JSON.stringify(rawValue ?? false),
      valueText: null,
    };
  }
  const valueText =
    rawValue == null || rawValue === '' ? null : String(rawValue);
  return { formFieldId, fieldKey: field.fieldKey, valueText, valueJson: null };
}

export interface InstanceState {
  step: number;
  instanceId: string | null;
  detail: ProcedureInstanceDetail | null;
  fieldValues: Record<string, unknown>;
  preflight: PreflightSnapshot | null;
  preflightLoading: boolean;
  riesgoAceptado: boolean;
  submitted: boolean;
  loading: boolean;
  error: string | null;
}

const INITIAL_STATE: InstanceState = {
  step: 0,
  instanceId: null,
  detail: null,
  fieldValues: {},
  preflight: null,
  preflightLoading: false,
  riesgoAceptado: false,
  submitted: false,
  loading: false,
  error: null,
};

/**
 * Orquesta el runtime de una instancia de trámite:
 * start (POST create draft) → saveDraft (PATCH field-values) por step →
 * goToStep → submit. runConsulta llama la consulta real de fuentes (#10201).
 */
export function useProcedureInstance() {
  const [state, setState] = useState<InstanceState>(INITIAL_STATE);

  // Ref sincronizada con el último estado: permite leer instanceId fresco
  // dentro de callbacks estables (sin recrearlos por cada cambio de estado).
  const stateRef = useRef(state);
  useEffect(() => {
    stateRef.current = state;
  }, [state]);

  const reset = useCallback(() => setState(INITIAL_STATE), []);

  const setFieldValue = useCallback((fieldKey: string, value: unknown) => {
    setState((s) => ({
      ...s,
      fieldValues: { ...s.fieldValues, [fieldKey]: value },
    }));
  }, []);

  const setRiesgoAceptado = useCallback((v: boolean) => {
    setState((s) => ({ ...s, riesgoAceptado: v }));
  }, []);

  const clearError = useCallback(() => {
    setState((s) => ({ ...s, error: null }));
  }, []);

  // Crea la instancia draft. M0: si se pasa `modalidad`, el backend deriva la
  // tipología y NO se envía procedureTypeId. El flujo legacy (selector de tipos
  // publicados) sigue funcionando pasando un procedureTypeId.
  const start = useCallback(
    async (
      target: { modalidad: WizardModalidad } | { procedureTypeId: string },
    ) => {
      setState((s) => ({ ...s, loading: true, error: null }));
      // El tenant/usuario del create SALEN del JWT del usuario autenticado, no de constantes de dev.
      // Para un usuario de compañía el backend igual lo impone desde el token; para un SuperAdmin (que
      // no manda X-Tenant-Id aquí) el backend usa ESTE tenant del body → debe ser su compañía real, no
      // el "Flit Dev Tenant" (DEV_TENANT_ID). Con el hardcode, el SuperAdmin creaba siempre contra ese
      // tenant fantasma —sin política de matrícula— y el gate fallaba aunque habilitara su empresa.
      // Fallback a las constantes de dev solo cuando no hay sesión (dev local sin auth).
      const payload = decodeJwtPayload(getToken());
      const body: CreateInstanceRequest = {
        tenantId: payload?.tenant_id ?? DEV_TENANT_ID,
        createdByUserId: payload?.sub ?? DEV_USER_ID,
        ...('modalidad' in target
          ? { modalidad: target.modalidad }
          : { procedureTypeId: target.procedureTypeId }),
      };
      try {
        const summary = await tramitesClient.createInstance(body);
        setState((s) => ({ ...s, instanceId: summary.id, loading: false, step: 0 }));
        return summary;
      } catch (err) {
        setState((s) => ({
          ...s,
          loading: false,
          error: err instanceof Error ? err.message : 'Error al iniciar el trámite',
        }));
        return null;
      }
    },
    [],
  );

  const saveDraft = useCallback(
    async (items: FieldValueInput[]) => {
      const id = stateRef.current.instanceId;
      if (!id) return null;
      setState((s) => ({ ...s, loading: true, error: null }));
      try {
        const detail = await tramitesClient.patchFieldValues(id, items);
        setState((s) => ({ ...s, detail, loading: false }));
        return detail;
      } catch (err) {
        setState((s) => ({
          ...s,
          loading: false,
          error: err instanceof Error ? err.message : 'Error al guardar el borrador',
        }));
        return null;
      }
    },
    [],
  );

  const goToStep = useCallback((step: number) => {
    setState((s) => ({ ...s, step }));
  }, []);

  const submit = useCallback(async () => {
    const id = stateRef.current.instanceId;
    if (!id) return null;
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      const summary = await tramitesClient.submitInstance(id);
      setState((s) => ({ ...s, loading: false, submitted: true }));
      return summary;
    } catch (err) {
      setState((s) => ({
        ...s,
        loading: false,
        error: err instanceof Error ? err.message : 'Error al enviar el trámite',
      }));
      return null;
    }
  }, []);

  // HU #10885 (Feature #10862, CF-04) — `forceRefresh` (AC2, botón "Actualizar") salta el reúso de
  // caché del backend (ADR-0030) y fuerza una consulta nueva; default false (comportamiento previo).
  const runConsulta = useCallback(async (forceRefresh = false) => {
    const instanceId = stateRef.current.instanceId;
    if (!instanceId) return null;
    setState((s) => ({ ...s, preflightLoading: true }));
    try {
      // TODO #10201: 'RUNT_VEHICLE' hardcoded (MVP) — debería venir de la
      // config del trámite (consultationTemplateId del field/step).
      const snapshot = await tramitesClient.runConsultation(
        instanceId,
        'RUNT_VEHICLE',
        undefined,
        forceRefresh,
      );
      setState((s) => ({
        ...s,
        preflight: snapshot,
        preflightLoading: false,
        // si baja de rojo, resetea aceptación de riesgo
        riesgoAceptado: snapshot.overall === 'red' ? s.riesgoAceptado : false,
      }));
      return snapshot;
    } catch {
      setState((s) => ({ ...s, preflightLoading: false }));
      return null;
    }
  }, []);

  return {
    state,
    reset,
    setFieldValue,
    setRiesgoAceptado,
    clearError,
    start,
    saveDraft,
    goToStep,
    submit,
    runConsulta,
  };
}
