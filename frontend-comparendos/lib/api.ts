// Cliente HTTP de Comparendos. El navegador siempre habla con este mismo host: /api/v1/* pasa por el proxy de
// @flit/auth (app/api/v1/[...path]), que pone el Bearer de la sesión y lo reenvía al gateway.
import { isReauthenticating, reauthenticate } from "@flit/auth/client";

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly code?: string,
    readonly data?: unknown,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export type Query = Record<string, string | number | boolean | string[] | undefined | null>;

export interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: unknown;
  query?: Query;
  signal?: AbortSignal;
}

export const API_PREFIX = "/api/v1/comparendos";

/** URL relativa con la query serializada (los arrays repiten el parámetro, como espera Minimal API). */
export function buildUrl(path: string, query?: Query): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query ?? {})) {
    if (value === undefined || value === null || value === "") continue;
    if (Array.isArray(value)) value.forEach((item) => params.append(key, item));
    else params.set(key, String(value));
  }
  const qs = params.toString();
  return `${API_PREFIX}${path}${qs ? `?${qs}` : ""}`;
}

export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", body, query, signal } = options;
  if (isReauthenticating()) return waitForReload<T>();

  const isForm = typeof FormData !== "undefined" && body instanceof FormData;
  const response = await fetch(buildUrl(path, query), {
    method,
    headers: body === undefined || isForm ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : isForm ? body : JSON.stringify(body),
    signal,
  });
  return readResponse<T>(response);
}

/** Resultado de una descarga: el archivo ya se entregó, o la API la aceptó para generarla en segundo plano (202). */
export interface ResultadoDescarga {
  aceptado: boolean;
  mensaje?: string;
}

/** Descarga un archivo (Excel, PDF) de la API y lo entrega al navegador con el nombre que diga el servidor. */
export async function apiDownload(path: string, fallbackName: string, query?: Query): Promise<ResultadoDescarga> {
  const response = await fetch(buildUrl(path, query));
  if (response.status === 202) {
    const data = (await safeJson(response)) as { mensaje?: string } | null;
    return { aceptado: true, mensaje: data?.mensaje };
  }
  if (!response.ok) await readResponse<never>(response);
  const blob = await response.blob();
  const disposition = response.headers.get("content-disposition") ?? "";
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const name = match ? decodeURIComponent(match[1]) : fallbackName;
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  link.click();
  URL.revokeObjectURL(url);
  return { aceptado: false };
}

async function readResponse<T>(response: Response): Promise<T> {
  if (response.status === 204) return undefined as T;

  const data = (await safeJson(response)) as
    | { code?: string; error?: string; detail?: string; title?: string; errors?: Record<string, string[]> }
    | null;
  if (response.ok) return data as T;

  if (response.status === 401) {
    // La sesión venció o la revocaron: se pide una nueva al hub y la página se recarga sola.
    reauthenticate({ silent: true });
    if (isReauthenticating()) return waitForReload<T>();
  }

  throw new ApiError(response.status, friendlyMessage(response.status, data), data?.code, data);
}

function friendlyMessage(
  status: number,
  data: { error?: string; detail?: string; title?: string; errors?: Record<string, string[]> } | null,
): string {
  const validation = data?.errors ? Object.values(data.errors).flat()[0] : undefined;
  const message = data?.error ?? data?.detail ?? validation ?? data?.title;
  if (message) return message;
  if (status === 403) return "No tienes permiso para esta acción.";
  if (status === 404) return "No se encontró lo que buscas.";
  if (status >= 500) return "El servicio de comparendos no respondió. Intenta de nuevo en un momento.";
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
