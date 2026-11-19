// Cliente tipado del dominio del portal Persona Natural del organismo (Épica #14297, Feature #14380, HU #14385).
// Contrato: docs/design/EPICA-14297-contrato-resto.md → grupo `/api/v1/admin/ot/portal-dominio`.
// El `ot_admin` trabaja con el organismo de su token y el SuperAdmin lo indica con `?transitOfficeId=`. Los errores del
// contrato son `{ code, message }` con el motivo exacto en español: se conservan tal cual.
import { clearToken, emitSessionExpired } from "@/lib/auth/session";
import { getToken, resolveApiUrl } from "./client";
import { ApiError, type TenantDomainResponse, type TenantDomainStatus } from "./types";

const base = "/api/v1/admin/ot/portal-dominio";

export interface PortalDominioScope {
  transitOfficeId?: string;
}

export type PortalDominioEstado = "pendiente" | "verificado" | "activo" | "fallido";

export interface PortalDominioDns {
  tipo: string;
  nombre: string;
  valor: string;
}

export interface DominioPortal {
  id: string;
  host: string;
  /** El organismo usa el principal activo en el enlace, el QR y los correos. */
  principal: boolean;
  estado: PortalDominioEstado;
  motivo: string | null;
  dns: PortalDominioDns[];
  rowVersion: number;
}

const GENERIC = "No se pudo completar la solicitud. Inténtalo de nuevo.";

function withScope(path: string, scope?: PortalDominioScope): string {
  if (!scope?.transitOfficeId) return path;
  const url = new URL(path, "http://relative.invalid");
  if (!url.searchParams.has("transitOfficeId"))
    url.searchParams.set("transitOfficeId", scope.transitOfficeId);
  return `${url.pathname}${url.search}`;
}

async function readBody(response: Response): Promise<{ code?: unknown; message?: unknown } | null> {
  try {
    return (await response.json()) as { code?: unknown; message?: unknown } | null;
  } catch {
    return null;
  }
}

/** Motivo exacto que manda el servidor (`message`); sin él, un texto genérico que no filtra ruta ni status. */
async function toApiError(response: Response): Promise<ApiError> {
  const body = await readBody(response);
  if (response.status === 401) {
    clearToken();
    emitSessionExpired();
    return new ApiError(401, "Tu sesión expiró. Vuelve a iniciar sesión.", body);
  }
  const message = typeof body?.message === "string" && body.message.trim() ? body.message : GENERIC;
  return new ApiError(response.status, message, body);
}

async function request(
  method: "GET" | "POST" | "PUT" | "DELETE",
  path: string,
  scope: PortalDominioScope | undefined,
  json?: unknown,
): Promise<Response> {
  const headers: Record<string, string> = {};
  const token = getToken();
  if (token) headers.Authorization = `Bearer ${token}`;
  if (json !== undefined) headers["Content-Type"] = "application/json";

  let response: Response;
  try {
    response = await fetch(resolveApiUrl(withScope(path, scope)), {
      method,
      headers,
      body: json === undefined ? undefined : JSON.stringify(json),
    });
  } catch {
    throw new ApiError(
      0,
      "No se pudo conectar con el servidor. Revisa tu conexión e inténtalo de nuevo.",
    );
  }
  return response;
}

async function requireDomain(response: Response): Promise<DominioPortal> {
  if (!response.ok) throw await toApiError(response);
  return (await response.json()) as DominioPortal;
}

/** GET — dominios del organismo (hasta 10). Lista vacía si no tiene ninguno. */
export async function getPortalDominios(scope?: PortalDominioScope): Promise<DominioPortal[]> {
  const response = await request("GET", base, scope);
  if (response.status === 404 || response.status === 204) return [];
  if (!response.ok) throw await toApiError(response);
  return ((await response.json()) as DominioPortal[] | null) ?? [];
}

/** POST { host } — agrega un dominio; queda pendiente con sus instrucciones de DNS. */
export async function registerPortalDominio(
  host: string,
  scope?: PortalDominioScope,
): Promise<DominioPortal> {
  return requireDomain(await request("POST", base, scope, { host }));
}

/** POST /{id}/verificar — comprobación a demanda del TXT. */
export async function verifyPortalDominio(
  id: string,
  scope?: PortalDominioScope,
): Promise<DominioPortal> {
  return requireDomain(await request("POST", `${base}/${encodeURIComponent(id)}/verificar`, scope));
}

/** PUT /{id}/principal — el dominio activo pasa a ser el que usan enlace, QR y correos. */
export async function setPortalDominioPrincipal(
  id: string,
  scope?: PortalDominioScope,
): Promise<DominioPortal> {
  return requireDomain(await request("PUT", `${base}/${encodeURIComponent(id)}/principal`, scope));
}

/** DELETE /{id} — retira el dominio; el portal sigue en los otros o en el enlace del punto. */
export async function deletePortalDominio(id: string, scope?: PortalDominioScope): Promise<void> {
  const response = await request("DELETE", `${base}/${encodeURIComponent(id)}`, scope);
  if (!response.ok) throw await toApiError(response);
}

/** Dominio principal y activo, o `null` (otro no se inventa). */
export function principalActivo(dominios: DominioPortal[]): DominioPortal | null {
  return dominios.find((d) => d.principal && d.estado === "activo") ?? null;
}

const ESTADOS: Record<PortalDominioEstado, TenantDomainStatus> = {
  pendiente: "pending",
  verificado: "verified",
  activo: "active",
  fallido: "failed",
};

/** Adapta el dominio del portal a la forma que ya pintan `DomainStatusPanel` y `DomainRegisterForm`. */
export function toTenantDomain(dominio: DominioPortal): TenantDomainResponse {
  const txt = dominio.dns.find((r) => r.tipo.toUpperCase() === "TXT");
  const a = dominio.dns.find((r) => r.tipo.toUpperCase() === "A");
  return {
    host: dominio.host,
    status: ESTADOS[dominio.estado],
    statusReason: dominio.motivo,
    statusChangedAt: "",
    verification: {
      txtName: txt?.nombre ?? "",
      txtValue: txt?.valor ?? "",
      cnameName: "",
      cnameTarget: "",
      aName: a?.nombre ?? dominio.host,
      aValue: a?.valor ?? "",
    },
    verifiedAt: null,
    activatedAt: null,
    certificate: { issuedAt: null, expiresAt: null },
    lastCheckedAt: null,
    nextCheckAt: null,
    graceUntil: null,
    rowVersion: dominio.rowVersion,
  };
}
