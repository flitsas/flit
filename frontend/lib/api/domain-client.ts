// Cliente tipado del dominio propio de una red Marca Blanca (HU #12416/#12425/#12427). Cubre las
// dos bases del contrato (`contracts/openapi/core-api.v1.yaml`): SuperAdmin sobre una cabeza
// puntual (`/admin/companies/{tenantId}/domain`) y autogestión de la propia cabeza, de solo
// lectura salvo `/verify` (`/company/domain`). Vive aparte de `branding.ts` a propósito: los
// endpoints `/verify` los está terminando el backend en paralelo (#12425) mientras se implementa
// esta HU — si su forma cambia, el ajuste queda contenido en este archivo.
//
// Uso de ejemplo:
//   const domain = await getAdminDomain(tenantId); // null si la red no tiene dominio (404)
//   await registerAdminDomain(tenantId, { host: "app.movilidadandina.com", rowVersion: domain?.rowVersion ?? null });
//   const result = await verifyAdminDomain(tenantId); // dispara comprobación a demanda
import { getToken, resolveApiUrl } from "./client";
import { ApiError, type TenantDomainResponse } from "./types";

/** Códigos estables — `Flit.Admin.Domain/Companies/Domains/DomainErrors.cs` (fuente de verdad del backend). */
export const DOMAIN_ERROR_MESSAGES: Record<string, string> = {
  DOMAIN_NOT_FOUND: "Esta red aún no tiene un dominio registrado.",
  DOMAIN_HOST_INVALID: "El dominio no tiene un formato válido. Usa solo el nombre de host, sin esquema ni puerto.",
  DOMAIN_HOST_RESERVED: "Ese dominio está reservado para FLIT y no puede registrarse.",
  DOMAIN_TENANT_NOT_MARCA_BLANCA: "Esta compañía no es una cabeza de red Marca Blanca.",
  DOMAIN_HOST_ALREADY_REGISTERED: "Ese dominio ya está registrado por otra red.",
  DOMAIN_ALREADY_REGISTERED_FOR_TENANT: "Esta red ya tiene un dominio vigente. Recarga e inténtalo de nuevo.",
  CONCURRENCY_CONFLICT: "Alguien más modificó este dominio. Vuelve a cargarlo e inténtalo de nuevo.",
  DOMAIN_VERIFICATION_COOLDOWN: "Ya se pidió una comprobación hace poco.",
};

const FALLBACK_MESSAGE = "No se pudo completar la solicitud. Inténtalo de nuevo.";

/** Texto único por código de error del dominio (mismo mapa para 400/403/404/409). */
export function domainErrorMessage(code: string | null | undefined): string {
  if (!code) return FALLBACK_MESSAGE;
  return DOMAIN_ERROR_MESSAGES[code] ?? FALLBACK_MESSAGE;
}

/** Código de error (`DOMAIN_*`, `CONCURRENCY_CONFLICT`) de un `ApiError`, si lo trae. */
export function domainErrorCode(error: unknown): string | null {
  if (!(error instanceof ApiError)) return null;
  const body = error.body as { error?: unknown; code?: unknown } | null | undefined;
  const code = body?.error ?? body?.code;
  return typeof code === "string" ? code : null;
}

/** `true` si el error es "esta red no tiene dominio" (404 `DOMAIN_NOT_FOUND`). */
export function isDomainNotFound(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404;
}

export interface RegisterDomainRequest {
  host: string;
  /** Ausente en el primer registro. */
  rowVersion?: number | null;
}

async function readErrorBody(response: Response): Promise<Record<string, unknown> | null> {
  try {
    return (await response.json()) as Record<string, unknown>;
  } catch {
    return null;
  }
}

function extractCode(body: Record<string, unknown> | null): string | undefined {
  if (!body) return undefined;
  if (typeof body.error === "string") return body.error;
  if (typeof body.code === "string") return body.code;
  return undefined;
}

/**
 * Segundos de espera de un 429 `DOMAIN_VERIFICATION_COOLDOWN`: cabecera `Retry-After` primero,
 * luego `retryAfterSeconds` del cuerpo. `null` si el backend no informa ninguno de los dos — en
 * ese caso el caller muestra un mensaje genérico en vez de un número inventado.
 */
function retryAfterSeconds(response: Response, body: Record<string, unknown> | null): number | null {
  const header = response.headers.get("Retry-After");
  const fromHeader = header !== null ? Number(header) : NaN;
  if (!Number.isNaN(fromHeader) && fromHeader >= 0) {
    return fromHeader;
  }
  const fromBody = body?.retryAfterSeconds;
  return typeof fromBody === "number" && fromBody >= 0 ? fromBody : null;
}

/** `ApiError` de un 429 de comprobación — mensaje ya formado con el tiempo de espera si se conoce. */
export function cooldownMessage(error: unknown): string | null {
  if (!(error instanceof ApiError) || error.status !== 429) return null;
  return error.message;
}

/** Segundos de espera de un 429 `DOMAIN_VERIFICATION_COOLDOWN`, si el backend los informó. */
export function cooldownSecondsFromError(error: unknown): number | null {
  if (!(error instanceof ApiError) || error.status !== 429) return null;
  const body = error.body as { retryAfterSeconds?: unknown } | null | undefined;
  return typeof body?.retryAfterSeconds === "number" && body.retryAfterSeconds >= 0
    ? body.retryAfterSeconds
    : null;
}

async function request<T>(
  path: string,
  method: "GET" | "PUT" | "DELETE" | "POST",
  body?: unknown,
): Promise<T | null> {
  const token = getToken();
  const headers: Record<string, string> = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers["Content-Type"] = "application/json";

  const response = await fetch(resolveApiUrl(path), {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (response.status === 404) {
    return null;
  }

  if (response.status === 204) {
    return undefined as T;
  }

  if (!response.ok) {
    const data = await readErrorBody(response);
    const code = extractCode(data);

    if (response.status === 429) {
      const retryAfter = retryAfterSeconds(response, data);
      const message =
        retryAfter != null
          ? `Ya se comprobó recientemente. Vuelve a intentarlo en ${retryAfter} s.`
          : domainErrorMessage(code ?? "DOMAIN_VERIFICATION_COOLDOWN");
      throw new ApiError(429, message, data);
    }

    throw new ApiError(response.status, domainErrorMessage(code), data);
  }

  return (await response.json()) as T;
}

// ── SuperAdmin sobre una cabeza puntual — /api/v1/admin/companies/{tenantId}/domain ────────────

/** GET .../domain (HU #12416 AC1, #12427 AC3). `null` = la red no tiene dominio (404). */
export async function getAdminDomain(tenantId: string): Promise<TenantDomainResponse | null> {
  return request<TenantDomainResponse>(`/api/v1/admin/companies/${tenantId}/domain`, "GET");
}

/** PUT .../domain — registra o cambia el host (HU #12416 AC1/AC2/AC3/AC5). */
export async function registerAdminDomain(
  tenantId: string,
  body: RegisterDomainRequest,
): Promise<TenantDomainResponse> {
  const result = await request<TenantDomainResponse>(`/api/v1/admin/companies/${tenantId}/domain`, "PUT", body);
  if (!result) {
    throw new ApiError(404, domainErrorMessage("DOMAIN_NOT_FOUND"));
  }
  return result;
}

/** DELETE .../domain — retira el dominio vigente (HU #12416 AC5). */
export async function removeAdminDomain(tenantId: string): Promise<void> {
  await request<void>(`/api/v1/admin/companies/${tenantId}/domain`, "DELETE");
}

/**
 * POST .../domain/verify — comprobación a demanda (HU #12425 AC2). El backend está terminando
 * este endpoint en paralelo (#12425): el contrato asumido es `200 TenantDomainResponse` con el
 * estado resultante y `429 { error: "DOMAIN_VERIFICATION_COOLDOWN", retryAfterSeconds? }` si se
 * supera el cooldown manual — ver supuestos documentados en
 * `.claude/state/marca-blanca/diseno/delta-hechos-post-adr.md`.
 */
export async function verifyAdminDomain(tenantId: string): Promise<TenantDomainResponse> {
  const result = await request<TenantDomainResponse>(`/api/v1/admin/companies/${tenantId}/domain/verify`, "POST");
  if (!result) {
    throw new ApiError(404, domainErrorMessage("DOMAIN_NOT_FOUND"));
  }
  return result;
}

// ── Autogestión de la cabeza (solo lectura salvo /verify) — /api/v1/company/domain ─────────────

/** GET /company/domain — solo lectura, registrar/cambiar/retirar es exclusivo del SuperAdmin (HU #12427 AC1/AC2). */
export async function getCompanyDomain(): Promise<TenantDomainResponse | null> {
  return request<TenantDomainResponse>("/api/v1/company/domain", "GET");
}

/** POST /company/domain/verify — misma comprobación a demanda que la de SuperAdmin (HU #12427 AC2). */
export async function verifyCompanyDomain(): Promise<TenantDomainResponse> {
  const result = await request<TenantDomainResponse>("/api/v1/company/domain/verify", "POST");
  if (!result) {
    throw new ApiError(404, domainErrorMessage("DOMAIN_NOT_FOUND"));
  }
  return result;
}

// ── Validación de host en cliente (AC5 del formulario — RFC 1123 básica) ───────────────────────

const HOST_PATTERN =
  /^(?=.{1,253}$)(?!-)[a-z0-9-]{1,63}(?<!-)(\.(?!-)[a-z0-9-]{1,63}(?<!-))+$/;

/**
 * Validación básica de formato en cliente (RFC 1123, minúsculas, sin esquema/puerto/ruta). No
 * sustituye la validación del servidor (IDN/punycode, reservados) — solo evita un viaje de red
 * para errores obvios de tecleo.
 */
export function isValidHostFormat(host: string): boolean {
  const trimmed = host.trim();
  if (!trimmed || trimmed !== trimmed.toLowerCase()) return false;
  if (trimmed.includes("://") || trimmed.includes("/") || trimmed.includes(":")) return false;
  return HOST_PATTERN.test(trimmed);
}
