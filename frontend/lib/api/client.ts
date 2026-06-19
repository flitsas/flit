// Cliente HTTP base contra el Gateway FLIT (HU #10194). Resuelve el token JWT,
// adjunta el header Authorization y normaliza errores 422 a ApiValidationError.
import { TOKEN_COOKIE, TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { clearToken, emitSessionExpired } from "@/lib/auth/session";
import { ApiError, ApiValidationError, type ValidationErrorResponse } from "./types";

export const API_BASE_URL =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:4002";

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
  query?: Record<string, string | number | boolean | undefined | null>;
  signal?: AbortSignal;
}

/**
 * Ejecuta una petición JSON contra la API. Lanza ApiValidationError en 422
 * (con `errors[]`), Error genérico en el resto de fallos. Devuelve `undefined`
 * en 204.
 */
export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", body, query, signal } = options;
  const url = new URL(path, API_BASE_URL);

  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== "") {
        url.searchParams.set(key, String(value));
      }
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
    const data = (await safeJson(response)) as ValidationErrorResponse | null;
    throw new ApiValidationError(data?.errors ?? [], 422);
  }

  if (!response.ok) {
    if (response.status === 401) {
      // HU #10172 AC2 — sesión expirada: limpia el token y avisa al modal global.
      const data = (await safeJson(response)) as { code?: string } | null;
      if (data?.code === "SESSION_EXPIRED") {
        clearToken();
        emitSessionExpired();
        throw new ApiError(401, "SESSION_EXPIRED");
      }
    }
    throw new ApiError(response.status, `Error ${response.status} al llamar ${path}`);
  }

  return (await safeJson(response)) as T;
}

async function safeJson(response: Response): Promise<unknown> {
  const text = await response.text();
  return text ? JSON.parse(text) : null;
}

function readCookie(name: string): string | null {
  const match = document.cookie.match(new RegExp(`(?:^|; )${name}=([^;]*)`));
  return match ? decodeURIComponent(match[1]) : null;
}

function base64Url(value: string): string {
  const b64 =
    typeof btoa === "function"
      ? btoa(value)
      : Buffer.from(value, "utf8").toString("base64");
  return b64.replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
