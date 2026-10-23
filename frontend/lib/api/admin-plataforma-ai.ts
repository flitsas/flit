// Cliente SuperAdmin — Plataforma → Configuración de IA (Feature #13508, HU #13511/#13512).
// Contrato: DISENO-TECNICO.md §5 + DTOs vigentes en AdminPlataformaAiEndpoints
// (`HasApiKey` / `ApiKeyMask`). Transitions y alerts pueden llegar después en backend;
// el cliente tolera 404/503 con estados vacíos/error en UI.
import { apiFetch } from "./client";
import { ApiError } from "./types";

const base = "/api/v1/admin/plataforma/ai";

export type AiToolCode = "ocr" | "dr_flit" | (string & {});
export type AiProviderCode = "anthropic" | "openai" | "kimi" | "deepseek" | (string & {});
export type AiTransitionReason = "manual" | "failover_auto" | "auto_return" | "seed" | (string & {});
export type AiProviderHealth = "unknown" | "healthy" | "degraded" | "down" | (string & {});

export interface AiProviderKeyStatus {
  providerCode: AiProviderCode;
  /** Key presente en env (backend: `hasApiKey`). */
  hasApiKey: boolean;
  /** Máscara si hay key; nunca el secreto (backend: `apiKeyMask`). */
  apiKeyMask: string | null;
  capable: boolean;
  enabled: boolean;
  priorityRank: number;
  health?: AiProviderHealth;
}

export interface AiToolSettingsView {
  toolCode: AiToolCode;
  activeProvider: AiProviderCode;
  autoReturnToPrimary: boolean;
  providers: AiProviderKeyStatus[];
  rowVersion?: number;
  updatedAt: string;
  updatedBy: string | null;
}

export interface AiToolsListView {
  tools: AiToolSettingsView[];
}

export interface AiToolSettingsPutRequest {
  autoReturnToPrimary: boolean;
  providers: Array<{
    providerCode: AiProviderCode;
    enabled: boolean;
    priorityRank: number;
  }>;
}

export interface AiActivateProviderRequest {
  providerCode: AiProviderCode;
  reason?: string;
}

export interface AiProviderTransitionView {
  id: string;
  toolCode: AiToolCode;
  fromProvider: AiProviderCode | null;
  toProvider: AiProviderCode;
  reason: AiTransitionReason;
  detail: string | null;
  actorUserId: string | null;
  occurredAt: string;
}

export interface AiTransitionsPage {
  items: AiProviderTransitionView[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface AiFailoverAlertView {
  id: string;
  toolCode: AiToolCode;
  fromProvider: AiProviderCode;
  toProvider: AiProviderCode;
  occurredAt: string;
  acked: boolean;
}

/** `GET /api/v1/admin/plataforma/ai/tools` */
export function listAiTools(signal?: AbortSignal): Promise<AiToolsListView> {
  return apiFetch<AiToolsListView>(`${base}/tools`, { method: "GET", signal });
}

/** `GET /api/v1/admin/plataforma/ai/tools/{toolCode}` */
export function getAiTool(toolCode: string, signal?: AbortSignal): Promise<AiToolSettingsView> {
  return apiFetch<AiToolSettingsView>(`${base}/tools/${encodeURIComponent(toolCode)}`, {
    method: "GET",
    signal,
  });
}

/** `PUT /api/v1/admin/plataforma/ai/tools/{toolCode}/settings` */
export function putAiToolSettings(
  toolCode: string,
  body: AiToolSettingsPutRequest,
): Promise<AiToolSettingsView> {
  return apiFetch<AiToolSettingsView>(`${base}/tools/${encodeURIComponent(toolCode)}/settings`, {
    method: "PUT",
    body,
  });
}

/** `POST /api/v1/admin/plataforma/ai/tools/{toolCode}/activate` */
export function activateAiProvider(
  toolCode: string,
  body: AiActivateProviderRequest,
): Promise<AiToolSettingsView> {
  return apiFetch<AiToolSettingsView>(`${base}/tools/${encodeURIComponent(toolCode)}/activate`, {
    method: "POST",
    body,
  });
}

/**
 * `GET /api/v1/admin/plataforma/ai/tools/{toolCode}/transitions?page=&pageSize=`
 * Default pageSize 20 (AC HU #13512).
 */
export async function listAiTransitions(
  toolCode: string,
  page = 1,
  pageSize = 20,
  signal?: AbortSignal,
): Promise<AiTransitionsPage> {
  const qs = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });
  const data = await apiFetch<Partial<AiTransitionsPage> & { items?: AiProviderTransitionView[] }>(
    `${base}/tools/${encodeURIComponent(toolCode)}/transitions?${qs}`,
    { method: "GET", signal },
  );
  return {
    items: data.items ?? [],
    page: data.page ?? page,
    pageSize: data.pageSize ?? pageSize,
    totalCount: data.totalCount ?? data.items?.length ?? 0,
  };
}

/** `GET /api/v1/admin/plataforma/ai/alerts` */
export async function listAiAlerts(signal?: AbortSignal): Promise<AiFailoverAlertView[]> {
  const data = await apiFetch<{ items?: AiFailoverAlertView[] } | AiFailoverAlertView[]>(
    `${base}/alerts`,
    { method: "GET", signal },
  );
  if (Array.isArray(data)) return data;
  return data.items ?? [];
}

/** `POST /api/v1/admin/plataforma/ai/alerts/{id}/ack` */
export function ackAiAlert(id: string): Promise<void> {
  return apiFetch<void>(`${base}/alerts/${encodeURIComponent(id)}/ack`, { method: "POST" });
}

export type ErrorAdminAi =
  | { tipo: "sin_permiso"; mensaje: string }
  | { tipo: "no_encontrado"; mensaje: string }
  | { tipo: "invalido"; mensaje: string; codigo?: string }
  | { tipo: "otro"; mensaje: string };

const MENSAJE_GENERICO = "No se pudo completar la solicitud. Inténtalo de nuevo.";
export const MENSAJE_SIN_PERMISO_IA =
  "Esta pantalla es solo para usuarios con el rol SuperAdmin activo.";
export const MENSAJE_HERRAMIENTA_NO_ENCONTRADA =
  "No se encontró la configuración de esta herramienta de IA.";

export function interpretarErrorAdminAi(err: unknown): ErrorAdminAi {
  if (!(err instanceof ApiError)) return { tipo: "otro", mensaje: MENSAJE_GENERICO };
  const cuerpo = (err.body && typeof err.body === "object" ? err.body : {}) as Record<string, unknown>;
  const codigo = typeof cuerpo.error === "string" ? cuerpo.error : undefined;

  if (err.status === 403) return { tipo: "sin_permiso", mensaje: MENSAJE_SIN_PERMISO_IA };
  if (err.status === 404) {
    return { tipo: "no_encontrado", mensaje: MENSAJE_HERRAMIENTA_NO_ENCONTRADA };
  }
  if (err.status === 400 || err.status === 422) {
    const mensajes: Record<string, string> = {
      proveedor_desconocido: "El proveedor indicado no existe en el catálogo.",
      proveedor_deshabilitado: "Ese proveedor está deshabilitado. Habilítalo antes de activarlo.",
      proveedor_no_aplica: "Ese proveedor no aplica a esta herramienta.",
      proveedor_sin_credencial: "Ese proveedor no tiene API key configurada en el entorno.",
      configuracion_invalida: "La configuración enviada no es válida.",
      proveedor_invalido: "Indica un proveedor válido.",
    };
    return {
      tipo: "invalido",
      mensaje: (codigo && mensajes[codigo]) || MENSAJE_GENERICO,
      codigo,
    };
  }
  return { tipo: "otro", mensaje: MENSAJE_GENERICO };
}
