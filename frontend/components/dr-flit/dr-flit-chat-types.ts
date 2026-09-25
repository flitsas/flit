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
