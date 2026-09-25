// Épica #12718 (ADR-0060) — cliente de DR. FLIT contra core-api: chat con LLM (HU #12926) y casos de
// soporte (Feature #12917). El tenant viaja en X-Tenant-Id igual que en el resto del runtime
// (`tenantHeader`); el backend, de todos modos, usa el del token para usuarios de compañía.
import type {
  DrFlitChatRequest,
  DrFlitChatResponse,
  DrFlitSupportAttachment,
  DrFlitSupportCaseCreated,
} from "@/components/dr-flit/dr-flit-chat-types";
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

/**
 * Feature #12917 — límites de adjuntos del caso de soporte. Son los defaults de
 * `DrFlit:SupportCase:*` del backend, que es quien manda: si allí se cambian, el backend rechaza con
 * 400 igual. Aquí sirven para avisar al usuario antes de subir (HU #12929 AC4).
 */
export const DR_FLIT_SUPPORT_MAX_ATTACHMENTS = 5;
export const DR_FLIT_SUPPORT_MAX_FILE_BYTES = 20 * 1024 * 1024;
export const DR_FLIT_SUPPORT_ALLOWED_EXTENSIONS = [".png", ".jpg", ".jpeg", ".webp", ".pdf", ".txt"] as const;

/** Error de radicación con el `code` del backend (p. ej. `support_unavailable` en el 502). */
export class DrFlitSupportCaseError extends ApiError {
  constructor(
    status: number,
    message: string,
    public readonly code: string | null,
    body?: unknown,
  ) {
    super(status, message, body);
    this.name = "DrFlitSupportCaseError";
  }
}

/** `POST /api/v1/dr-flit/support-cases/attachments` (multipart). */
export async function uploadSupportAttachment(
  file: File,
): Promise<DrFlitSupportAttachment> {
  const form = new FormData();
  form.append("file", file, file.name);
  const response = await fetch(resolveApiUrl("/api/v1/dr-flit/support-cases/attachments"), {
    method: "POST",
    headers: tenantHeader(),
    body: form,
  });
  const data = await readJson(response);
  if (!response.ok) {
    throw new ApiError(response.status, friendlyErrorMessage(data as Record<string, unknown> | null), data);
  }
  return data as DrFlitSupportAttachment;
}

/** Cuerpo de `POST /api/v1/dr-flit/support-cases` (contrato §5.2). */
export interface DrFlitSupportCaseRequest {
  nombre: string;
  email: string;
  telefono: string | null;
  compania: string;
  detalle: string;
  resultadoEsperado: string;
  frecuencia: string;
  titulo: string;
  prioridad: string;
  affectedModule: string | null;
  attachmentIds: string[];
}

/**
 * `POST /api/v1/dr-flit/support-cases`. Solo se llama tras el clic explícito en «Confirmar y radicar
 * caso» (guardarraíl ADR-0060 §8.2.3). Lanza {@link DrFlitSupportCaseError} con el `code` del backend.
 */
export async function createSupportCase(
  request: DrFlitSupportCaseRequest,
): Promise<DrFlitSupportCaseCreated> {
  const response = await fetch(resolveApiUrl("/api/v1/dr-flit/support-cases"), {
    method: "POST",
    headers: { ...tenantHeader(), "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  const data = (await readJson(response)) as Record<string, unknown> | null;
  if (!response.ok) {
    const code = typeof data?.code === "string" ? data.code : null;
    throw new DrFlitSupportCaseError(response.status, friendlyErrorMessage(data), code, data);
  }
  return data as unknown as DrFlitSupportCaseCreated;
}
