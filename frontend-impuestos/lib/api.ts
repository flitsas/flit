// Cliente HTTP de Impuestos. El navegador siempre habla con este mismo host: /api/v1/* pasa por el proxy de @flit/auth
// (app/api/v1/[...path]), que pone el Bearer de la sesión y lo reenvía al gateway. El proxy es genérico; las rutas de
// negocio las define el contrato contracts/openapi/impuestos.v1.yaml y las nombra cada módulo de lib/<módulo>/api.ts.
import { isReauthenticating, reauthenticate } from "@flit/auth/client";
import { conEmpresa } from "@/lib/empresaSeleccionada";
import { pareceCodigo } from "@/lib/rotulos";

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly code?: string,
    /** Cuerpo del error tal como llegó (p. ej. `{ tope }` de un 422), para que cada pantalla lea sus campos. */
    readonly detalle?: Readonly<Record<string, unknown>>,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: unknown;
  signal?: AbortSignal;
  cache?: RequestCache;
  /** Compañía elegida por el Super Admin (?empresa= de la URL): se propaga a la API. Vacía o null: no se envía. */
  empresa?: string | null;
}

export const API_PREFIX = "/api/v1/impuestos";

/**
 * Rutas del esqueleto (plan §5.1): la sesión, los avisos y la lista de compañías del selector del Super Admin. Las de
 * cada módulo de negocio viven en su lib/<módulo>/api.ts.
 */
export const RUTAS = {
  sesion: "/sesion",
  notificaciones: "/notificaciones",
  notificacionLeida: (id: string) => `/notificaciones/${encodeURIComponent(id)}/leida`,
  empresas: "/backoffice/empresas",
} as const;

export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", body, signal, cache, empresa } = options;
  if (isReauthenticating()) return waitForReload<T>();

  // Un FormData (multipart de la carga masiva) viaja tal cual: el navegador pone el Content-Type con su boundary.
  const multipart = typeof FormData !== "undefined" && body instanceof FormData;
  const response = await fetch(`${API_PREFIX}${conEmpresa(path, empresa)}`, {
    method,
    headers: body === undefined || multipart ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : multipart ? body : JSON.stringify(body),
    signal,
    ...(cache ? { cache } : {}),
  });
  if (response.status === 204) return undefined as T;

  const data = (await safeJson(response)) as ({ code?: string; error?: string; detail?: string; title?: string } & Record<string, unknown>) | null;
  if (response.ok) return data as T;

  if (response.status === 401) {
    // La sesión venció o la revocaron: se pide una nueva al hub y la página se recarga sola.
    reauthenticate({ silent: true });
    if (isReauthenticating()) return waitForReload<T>();
  }

  throw new ApiError(response.status, friendlyMessage(response.status, data), data?.code, data ?? undefined);
}

function friendlyMessage(status: number, data: { code?: string; error?: string; detail?: string; title?: string } | null): string {
  if (status >= 500) return "Impuestos no respondió. Intenta de nuevo en un momento.";
  // Un texto que es el código interno (o tiene su forma) nunca se muestra; se pasa al siguiente.
  const deNegocio = (texto?: string) => (texto && texto.trim() && texto !== data?.code && !pareceCodigo(texto) ? texto : undefined);
  const message = deNegocio(data?.error) ?? deNegocio(data?.detail) ?? deNegocio(data?.title);
  if (message) return message;
  if (status === 403) return "No tienes permiso para esta acción.";
  if (status === 404) return "No se encontró lo que buscas.";
  return "No se pudo completar la acción.";
}

async function safeJson(response: Response): Promise<unknown> {
  const text = await response.text();
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function waitForReload<T>(): Promise<T> {
  return new Promise<T>(() => {});
}
