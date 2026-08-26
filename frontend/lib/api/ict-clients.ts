// Cliente tipado de administración de clientes de integración ICT (ronda 2, Feature #10888).
// Consume el CRUD de core-ict a través del Gateway (JWT de plataforma, permiso ict.clients.manage):
//   GET/POST   /api/v1/ict/clients
//   PATCH      /api/v1/ict/clients/{id}
//   POST       /api/v1/ict/clients/{id}/reset-secret
import { apiFetch } from "./client";

export interface IctClient {
  id: string;
  tenantId: string;
  tenantName: string;
  username: string;
  /** Scopes como cadena JSON (p.ej. '["ict.transactions.write","ict.status.read"]'). */
  scopes: string;
  isActive: boolean;
  mustRotate: boolean;
  /** Modo prueba: la compañía NO consulta fuentes externas de pago; todo cae en Con Novedades. */
  testMode: boolean;
  lastLoginAt: string | null;
  lockedUntil: string | null;
  createdAt: string;
}

/** Resultado de crear/regenerar: el cliente + el secreto en claro (mostrar UNA sola vez). */
export interface IctClientSecret {
  client: IctClient;
  generatedSecret: string;
}

export function fetchIctClients(tenantId?: string, signal?: AbortSignal): Promise<{ items: IctClient[] }> {
  return apiFetch<{ items: IctClient[] }>("/api/v1/ict/clients", { query: { tenantId }, signal });
}

export function createIctClient(body: {
  username: string;
  tenantId: string;
  scopes?: string[];
  testMode?: boolean;
}): Promise<IctClientSecret> {
  return apiFetch<IctClientSecret>("/api/v1/ict/clients", { method: "POST", body });
}

export function updateIctClient(
  id: string,
  body: { isActive?: boolean; mustRotate?: boolean; scopes?: string[]; testMode?: boolean },
): Promise<IctClient> {
  return apiFetch<IctClient>(`/api/v1/ict/clients/${id}`, { method: "PATCH", body });
}

export function resetIctClientSecret(id: string): Promise<IctClientSecret> {
  return apiFetch<IctClientSecret>(`/api/v1/ict/clients/${id}/reset-secret`, { method: "POST" });
}
