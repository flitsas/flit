// HU #13200 (Feature #13065, Épica #12737) — cliente tipado de la administración de clientes de
// integración EXTERNOS (p. ej. Flito). Consume la API de la HU #13088 (SuperAdmin, JWT de plataforma):
//   GET/POST  /api/v1/admin/external-clients
//   PATCH     /api/v1/admin/external-clients/{id}
//   POST      /api/v1/admin/external-clients/{id}/regenerate-secret
//   POST      /api/v1/admin/external-clients/{id}/unlock
// No confundir con los clientes ICT (lib/api/ict-clients.ts): estos no pertenecen a una compañía y leen
// el feed de trámites de todas. El secreto solo llega en el alta y en la rotación, y se muestra UNA vez.
import { apiFetch } from "./client";

const BASE = "/api/v1/admin/external-clients";

/** Permisos que puede tener un cliente externo (ADR-0067). */
export const EXTERNAL_SCOPE_READ = "external.tramites.read";
export const EXTERNAL_SCOPE_PII = "external.tramites.pii.read";
/** Feature #13261 (HU #13263): envío del comprobante de impuesto (POST /external/tramites/{id}/adjuntos). */
export const EXTERNAL_SCOPE_ATTACHMENTS = "external.tramites.attachments.write";

/** Mismo formato que exige el backend (ck_external_clients_client_id_formato, DDL 125). */
export const EXTERNAL_CLIENT_ID_PATTERN = /^[a-z0-9][a-z0-9-]{2,63}$/;

export interface ExternalClient {
  id: string;
  clientId: string;
  displayName: string;
  purpose: string;
  scopes: string[];
  isActive: boolean;
  mustRotate: boolean;
  lockedUntil: string | null;
  lastTokenAt: string | null;
  createdAt: string;
}

/** Alta o rotación: el cliente + el secreto en claro (mostrar UNA sola vez, no guardar). */
export interface ExternalClientSecret {
  client: ExternalClient;
  clientSecret: string;
}

export interface NewExternalClient {
  clientId: string;
  displayName: string;
  purpose: string;
  scopes: string[];
}

export interface ExternalClientChanges {
  displayName?: string;
  purpose?: string;
  scopes?: string[];
  isActive?: boolean;
  mustRotate?: boolean;
}

export function fetchExternalClients(signal?: AbortSignal): Promise<ExternalClient[]> {
  return apiFetch<ExternalClient[]>(BASE, { signal });
}

export function createExternalClient(body: NewExternalClient): Promise<ExternalClientSecret> {
  return apiFetch<ExternalClientSecret>(BASE, { method: "POST", body });
}

export function updateExternalClient(id: string, body: ExternalClientChanges): Promise<ExternalClient> {
  return apiFetch<ExternalClient>(`${BASE}/${id}`, { method: "PATCH", body });
}

/** `revocarAnterior`: el secreto anterior deja de valer al instante (si no, sigue valiendo 24 h). */
export function regenerateExternalClientSecret(id: string, revocarAnterior: boolean): Promise<ExternalClientSecret> {
  return apiFetch<ExternalClientSecret>(`${BASE}/${id}/regenerate-secret`, {
    method: "POST",
    body: { revocarAnterior },
  });
}

export function unlockExternalClient(id: string): Promise<ExternalClient> {
  return apiFetch<ExternalClient>(`${BASE}/${id}/unlock`, { method: "POST" });
}

/** El cliente está bloqueado por intentos fallidos mientras `lockedUntil` sea futuro. */
export function isExternalClientLocked(client: ExternalClient, now: Date = new Date()): boolean {
  return client.lockedUntil !== null && new Date(client.lockedUntil).getTime() > now.getTime();
}
