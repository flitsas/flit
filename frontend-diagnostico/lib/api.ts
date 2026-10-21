// Cliente HTTP de Diagnóstico. El navegador siempre habla con este mismo host: /api/v1/* pasa por el proxy de
// @flit/auth (app/api/v1/[...path]), que pone el Bearer de la sesión y lo reenvía al gateway.
import { isReauthenticating, reauthenticate } from "@flit/auth/client";

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly code?: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: unknown;
  signal?: AbortSignal;
}

export const API_PREFIX = "/api/v1/diagnostico";

export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", body, signal } = options;
  if (isReauthenticating()) return waitForReload<T>();

  const response = await fetch(`${API_PREFIX}${path}`, {
    method,
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });
  if (response.status === 204) return undefined as T;

  const data = (await safeJson(response)) as { code?: string; error?: string; detail?: string; title?: string } | null;
  if (response.ok) return data as T;

  if (response.status === 401) {
    // La sesión venció o la revocaron: se pide una nueva al hub y la página se recarga sola.
    reauthenticate({ silent: true });
    if (isReauthenticating()) return waitForReload<T>();
  }

  throw new ApiError(response.status, friendlyMessage(response.status, data), data?.code);
}

function friendlyMessage(status: number, data: { error?: string; detail?: string; title?: string } | null): string {
  if (status >= 500) return "Diagnóstico no respondió. Intenta de nuevo en un momento.";
  const message = data?.error ?? data?.detail ?? data?.title;
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
