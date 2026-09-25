// Épica #12718 (ADR-0060) — cliente de DR. FLIT contra core-api: chat con LLM (HU #12926) y casos de
// soporte (Feature #12917). El tenant viaja en X-Tenant-Id igual que en el resto del runtime
// (`tenantHeader`); el backend, de todos modos, usa el del token para usuarios de compañía.
import type { DrFlitChatRequest, DrFlitChatResponse } from "@/components/dr-flit/dr-flit-chat-types";
import { friendlyErrorMessage, resolveApiUrl } from "./client";
import { tenantHeader } from "./tramites-client";
import { ApiError } from "./types";

/** Máximo de turnos previos que acepta el backend (`history.maxItems` del contrato). */
export const DR_FLIT_CHAT_MAX_HISTORY = 12;

/** Máximo de caracteres por mensaje y por turno (`maxLength` del contrato). */
export const DR_FLIT_CHAT_MAX_LENGTH = 2000;

async function readJson(response: Response): Promise<unknown> {
  const text = await response.text();
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

/**
 * `POST /api/v1/dr-flit/chat`. El LLM caído o el tope alcanzado NO son errores: llegan como 200 con
 * `status` degraded/rate_limited. Lanza solo ante fallos HTTP o de red; quien llama los trata como
 * degradado y responde con el buscador local.
 */
export async function postDrFlitChat(
  request: DrFlitChatRequest,
  signal?: AbortSignal,
): Promise<DrFlitChatResponse> {
  const response = await fetch(resolveApiUrl("/api/v1/dr-flit/chat"), {
    method: "POST",
    headers: { ...tenantHeader(), "Content-Type": "application/json" },
    body: JSON.stringify(request),
    signal,
  });
  const data = await readJson(response);
  if (!response.ok) {
    throw new ApiError(response.status, friendlyErrorMessage(data as Record<string, unknown> | null), data);
  }
  return data as DrFlitChatResponse;
}
