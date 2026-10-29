// HU #14068 (Feature #14067, Épica #14021) — cliente tipado de las integraciones externas LIGADAS A UNA COMPAÑÍA
// (p. ej. la de «Persona Natural»). Contrato: `contracts/openapi/core-api.v1.yaml` (schemas `ExternalIntegration` y
// `ExternalIntegrationSecret`), solo SuperAdmin:
//   GET   /api/v1/admin/companies/{companyId}/external-integrations
//   POST  /api/v1/admin/companies/{companyId}/external-integrations
//   POST  …/{id}/rotate-secret · …/{id}/block · …/{id}/unblock · …/{id}/deactivate
// No confundir con `lib/api/external-clients.ts` (clientes globales como Flito, sin compañía). El secreto solo
// llega en el alta y en la rotación y se muestra UNA vez: nunca se guarda en almacenamiento del navegador.
import { apiFetch } from "./client";

const base = (companyId: string) => `/api/v1/admin/companies/${companyId}/external-integrations`;

/** Tipos de integración por compañía que acepta la API hoy. */
export type ExternalIntegrationType = "persona_natural";

/** Permiso fijo de la integración de Persona Natural: solo lectura de la fachada del portal. */
export const PERSONA_NATURAL_SCOPE = "platform.tramites.persona_natural";

/** `ExternalIntegration`: nunca incluye el secreto ni sus hashes. */
export interface ExternalIntegration {
  id: string;
  clientId: string;
  displayName: string;
  purpose: string;
  scopes: string[];
  isActive: boolean;
  mustRotate: boolean;
  /** Con año 9999, bloqueo indefinido (manual); si no, bloqueo temporal por intentos fallidos. */
  lockedUntil: string | null;
  lastTokenAt: string | null;
  createdAt: string;
  tipo: ExternalIntegrationType;
  companyId: string;
}

/** Alta o rotación: la integración + el secreto en claro (mostrar UNA sola vez, no guardar). */
export interface ExternalIntegrationSecret {
  client: ExternalIntegration;
  clientSecret: string;
}

export interface NewExternalIntegration {
  tipo: ExternalIntegrationType;
  displayName?: string;
  purpose?: string;
}

/** Estado visible de la integración: la activa puede estar bloqueada; la desactivada no vuelve. */
export type ExternalIntegrationStatus = "active" | "blocked" | "inactive";

export function externalIntegrationStatus(
  integration: ExternalIntegration,
  now: Date = new Date(),
): ExternalIntegrationStatus {
  if (!integration.isActive) return "inactive";
  return isExternalIntegrationBlocked(integration, now) ? "blocked" : "active";
}

/** Bloqueada mientras `lockedUntil` sea futuro (el año 9999 es el bloqueo indefinido del administrador). */
export function isExternalIntegrationBlocked(integration: ExternalIntegration, now: Date = new Date()): boolean {
  return integration.lockedUntil !== null && new Date(integration.lockedUntil).getTime() > now.getTime();
}

export function fetchExternalIntegrations(companyId: string, signal?: AbortSignal): Promise<ExternalIntegration[]> {
  return apiFetch<ExternalIntegration[]>(base(companyId), { signal });
}

export function createExternalIntegration(
  companyId: string,
  body: NewExternalIntegration,
): Promise<ExternalIntegrationSecret> {
  return apiFetch<ExternalIntegrationSecret>(base(companyId), { method: "POST", body });
}

/** `revocarAnterior`: el secreto anterior deja de valer al instante (si no, sigue valiendo 24 h). */
export function rotateExternalIntegrationSecret(
  companyId: string,
  id: string,
  revocarAnterior: boolean,
): Promise<ExternalIntegrationSecret> {
  return apiFetch<ExternalIntegrationSecret>(`${base(companyId)}/${id}/rotate-secret`, {
    method: "POST",
    body: { revocarAnterior },
  });
}

export function blockExternalIntegration(companyId: string, id: string): Promise<ExternalIntegration> {
  return apiFetch<ExternalIntegration>(`${base(companyId)}/${id}/block`, { method: "POST" });
}

export function unblockExternalIntegration(companyId: string, id: string): Promise<ExternalIntegration> {
  return apiFetch<ExternalIntegration>(`${base(companyId)}/${id}/unblock`, { method: "POST" });
}

export function deactivateExternalIntegration(companyId: string, id: string): Promise<ExternalIntegration> {
  return apiFetch<ExternalIntegration>(`${base(companyId)}/${id}/deactivate`, { method: "POST" });
}
