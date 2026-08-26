import { request, tenantHeader } from './tramites-client';

/**
 * Preferencias de UI persistidas por usuario (selector de columnas de las tablas de trámites).
 * Contrato acordado con el equipo de backend (en desarrollo en paralelo, mismo shape):
 *   GET /api/v1/me/ui-preferences/{scope} → 200 { scope, value }. Sin preferencia guardada,
 *   `value` es `{}` — el backend NUNCA responde 404 aquí.
 *   PUT /api/v1/me/ui-preferences/{scope} body { value } → 200 con el mismo shape.
 * Reusa `request`/`tenantHeader` de tramites-client: mismo Bearer + X-Tenant-Id que el resto
 * del cliente de trámites, en vez de reimplementar la resolución de tenant.
 */
export type UiPreferenceScope = 'tramites.columns' | 'ot.procedures.columns';

/** Forma del valor persistido. Hoy solo se usa `visible` (claves de columna); se deja abierta
 *  a futuras claves de preferencia bajo el mismo scope. */
export interface UiPreferenceValue {
  visible?: string[];
  [key: string]: unknown;
}

export interface UiPreferenceResponse {
  scope: string;
  value: UiPreferenceValue;
}

export const uiPreferencesClient = {
  get: (scope: UiPreferenceScope): Promise<UiPreferenceResponse> =>
    request<UiPreferenceResponse>(`/api/v1/me/ui-preferences/${encodeURIComponent(scope)}`, {
      headers: tenantHeader(),
    }),

  put: (scope: UiPreferenceScope, value: UiPreferenceValue): Promise<UiPreferenceResponse> =>
    request<UiPreferenceResponse>(`/api/v1/me/ui-preferences/${encodeURIComponent(scope)}`, {
      method: 'PUT',
      headers: tenantHeader(),
      body: JSON.stringify({ value }),
    }),
};
