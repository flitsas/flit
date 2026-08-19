// Cliente HTTP base contra el Gateway FLIT (HU #10194). Resuelve el token JWT,
// adjunta el header Authorization y normaliza errores 422 a ApiValidationError.
import { TOKEN_COOKIE, TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { clearToken, emitSessionExpired } from "@/lib/auth/session";
import { ApiError, ApiValidationError, type ValidationErrorResponse } from "./types";

export const API_BASE_URL =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? "";

/**
 * Obtiene el JWT en cliente: primero cookie `flit_token`, luego localStorage
 * `flit:jwt`. En SSR/edge devuelve `null` (el middleware ya gobierna el acceso).
 */
export function getToken(): string | null {
  if (typeof document !== "undefined") {
    const fromCookie = readCookie(TOKEN_COOKIE);
    if (fromCookie) {
      return fromCookie;
    }
  }

  if (typeof window !== "undefined") {
    return window.localStorage.getItem(TOKEN_STORAGE_KEY);
  }

  return null;
}

/** Helper de desarrollo/tests: inyecta un JWT SuperAdmin mock en cookie + storage. */
export function setDevSuperAdminToken(sub = "11111111-1111-1111-1111-111111111111"): void {
  const header = base64Url(JSON.stringify({ alg: "none", typ: "JWT" }));
  const payload = base64Url(JSON.stringify({ sub, role: "SuperAdmin" }));
  const token = `${header}.${payload}.`;

  if (typeof document !== "undefined") {
    document.cookie = `${TOKEN_COOKIE}=${token}; path=/`;
  }
  if (typeof window !== "undefined") {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, token);
  }
}

export interface RequestOptions {
  method?: string;
  body?: unknown;
  /** Los arrays se serializan repitiendo el parámetro (`userIds=a&userIds=b`), que es lo que espera el binding de Minimal API. */
  query?: Record<string, string | number | boolean | string[] | undefined | null>;
  signal?: AbortSignal;
}

/**
 * Ejecuta una petición JSON contra la API. Lanza ApiValidationError en 422
 * (con `errors[]`), Error genérico en el resto de fallos. Devuelve `undefined`
 * en 204.
 */
export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", body, query, signal } = options;
  const base = API_BASE_URL || (typeof window !== "undefined" ? window.location.origin : "http://localhost:3000");
  const url = new URL(path, base);

  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value === undefined || value === null || value === "") {
        continue;
      }

      if (Array.isArray(value)) {
        for (const item of value) {
          url.searchParams.append(key, item);
        }
        continue;
      }

      url.searchParams.set(key, String(value));
    }
  }

  const token = getToken();
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  const response = await fetch(url.toString(), {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });

  if (response.status === 204) {
    return undefined as T;
  }

  if (response.status === 422) {
    const data = (await safeJson(response)) as
      | (ValidationErrorResponse & { detail?: string; title?: string })
      | null;
    // ProblemDetails (RFC 7807) con `detail`: no es el diccionario de validación de modelo.
    // Antes se convertía en ApiValidationError vacío y la UI perdía el motivo (p.ej. placa).
    if (typeof data?.detail === "string" && data.detail.trim()) {
      throw new ApiError(422, data.detail, data);
    }
    throw new ApiValidationError(data?.errors ?? [], 422);
  }

  if (!response.ok) {
    // Se lee el cuerpo una sola vez y se adjunta al ApiError para que el caller pueda
    // reaccionar a errores con detalle (p. ej. el 409 del soft-delete con `procedureTypes`).
    const data = (await safeJson(response)) as
      | { code?: string; error?: string; detail?: string; title?: string }
      | null;
    if (response.status === 401 && data?.code === "SESSION_EXPIRED") {
      // HU #10172 AC2 — sesión expirada: limpia el token y avisa al modal global.
      clearToken();
      emitSessionExpired();
      throw new ApiError(401, "SESSION_EXPIRED");
    }
    // El backend ya manda el motivo en español cuando lo tiene ({ error } de los Results.Conflict/
    // BadRequest/NotFound "a mano", o { detail } de un ProblemDetails con Results.Problem) — se
    // prioriza ese texto. Sin uno, el mensaje NUNCA debe filtrar la ruta/status técnico a quien lo
    // ve en pantalla: un componente que hace `catch { setError(e.message) }` sin más se volvería un
    // "Error 500 al llamar /api/v1/..." ilegible para el usuario.
    const friendly = data?.error ?? data?.detail ?? "No se pudo completar la solicitud. Inténtalo de nuevo.";
    throw new ApiError(response.status, friendly, data);
  }

  return (await safeJson(response)) as T;
}

async function safeJson(response: Response): Promise<unknown> {
  const text = await response.text();
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    // HU #10797 (AC5) — una respuesta no-JSON (p. ej. la página HTML de error de desarrollo:
    // "Microsoft.EntityFrameworkCore...") no debe romper el cliente con "Unexpected token". Se
    // ignora el cuerpo no parseable; el caller recibe un ApiError con el status (mensaje legible).
    return null;
  }
}

function readCookie(name: string): string | null {
  const prefix = `${name}=`;
  for (const part of document.cookie.split(";")) {
    const trimmed = part.trimStart();
    if (trimmed.startsWith(prefix)) {
      return decodeURIComponent(trimmed.slice(prefix.length));
    }
  }
  return null;
}

function base64Url(value: string): string {
  const b64 =
    typeof btoa === "function"
      ? btoa(value)
      : Buffer.from(value, "utf8").toString("base64");
  return b64.replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
