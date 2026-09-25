/**
 * Épica #12718 (ADR-0060) — contrato de DR. FLIT con el backend: chat con LLM y casos de soporte.
 * Espejo de `contracts/openapi/core-api.v1.yaml` (`/api/v1/dr-flit/*`).
 */

export type DrFlitChatStatus = "ok" | "degraded" | "rate_limited";

export type DrFlitChatIntent = "duda" | "soporte" | "gestion" | "no_claro";

export type DrFlitGestionSuggestion = "placa" | "vin" | "tramite" | "cliente";

export interface DrFlitCitation {
  slug: string;
  title: string;
  href: string;
  sourceHref: string | null;
  primarySource: boolean;
}

export interface DrFlitChatUsage {
  messagesUsedToday: number;
  dailyLimit: number;
}

export interface DrFlitChatResponse {
  status: DrFlitChatStatus;
  intent: DrFlitChatIntent;
  reply: string;
  citations: DrFlitCitation[];
  suggestGestionIntent: DrFlitGestionSuggestion | null;
  usage: DrFlitChatUsage;
}

export interface DrFlitChatTurn {
  role: "user" | "assistant";
  text: string;
}

export interface DrFlitChatRequest {
  message: string;
  history: DrFlitChatTurn[];
  routeScope?: string | null;
}

// ── Feature #12917 — caso de soporte (ADR-0060 §5.2 y §5.3) ────────────────────────────────────

export type DrFlitSupportFrequency = "una_vez" | "a_veces" | "siempre";

export type DrFlitSupportPriority = "Alta" | "Media" | "Baja";

/** Adjunto ya subido: en el estado solo vive su id, nunca el binario (sessionStorage). */
export interface DrFlitSupportAttachment {
  id: string;
  filename: string;
  sizeBytes: number;
}

/** Formulario del caso tal como lo va llenando el usuario. */
export interface DrFlitSupportCaseDraft {
  nombre: string;
  email: string;
  telefono: string;
  compania: string;
  /** Fecha del reporte (DD/MM/YYYY, hora Colombia): se fija al abrir el formulario. */
  fecha: string;
  detalle: string;
  resultadoEsperado: string;
  frecuencia: DrFlitSupportFrequency | "";
  titulo: string;
  prioridad: DrFlitSupportPriority | "";
  /** ¿Quiere adjuntar archivos? null = no ha respondido. */
  adjuntar: boolean | null;
  attachments: DrFlitSupportAttachment[];
  /** Módulo desde donde reporta (mejor esfuerzo); el backend lo valida contra su allow-list. */
  affectedModule: string | null;
}

/** Respuesta 201 de POST /api/v1/dr-flit/support-cases. */
export interface DrFlitSupportCaseCreated {
  caseId: number;
  /** Solo para SuperAdmin. */
  caseUrl: string | null;
  attachmentsFailed: number;
}
