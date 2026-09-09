/**
 * CF-21 — cuatro estados internos, TRES etiquetas de usuario.
 *
 * <p>Este es el ÚNICO archivo del módulo "Generación documental" que declara etiquetas de
 * estado. El backend maneja `pending`, `processing`, `generated` y `error`; la interfaz
 * muestra «Generado», «Error» y «En proceso» — `pending` y `processing` colapsan en la
 * misma etiqueta porque para el usuario no son dos cosas distintas (AC literal de HU-01).
 * Ningún otro componente, panel, tabla o filtro puede escribir estos textos: si hace falta
 * uno nuevo, se agrega aquí.</p>
 *
 * <p>El estado NUNCA se comunica solo por color (CF-22): el `tone` acompaña a un texto
 * visible, no lo sustituye.</p>
 */
import type { StatusTone } from "@/components/atom/StatusBadge";

/** Los cuatro estados internos del backend (`admin.standalone_documents.status`). */
export const STANDALONE_DOCUMENT_STATUSES = ["pending", "processing", "generated", "error"] as const;

export type StandaloneDocumentStatus = (typeof STANDALONE_DOCUMENT_STATUSES)[number];

/** Las TRES opciones que ve el usuario (etiqueta + tono semántico). */
export type StandaloneDocumentStatusFilter = "generated" | "error" | "en_proceso";

export interface StandaloneDocumentStatusView {
  /** Texto visible. Siempre presente: el estado no se comunica solo por color. */
  label: string;
  /** Tono semántico de `StatusBadge` (paleta única de globals.css). */
  tone: StatusTone;
  /** Opción de filtro a la que pertenece el estado interno. */
  filter: StandaloneDocumentStatusFilter;
}

const STATUS_VIEW: Record<StandaloneDocumentStatus, StandaloneDocumentStatusView> = {
  generated: { label: "Generado", tone: "success", filter: "generated" },
  error: { label: "Error", tone: "danger", filter: "error" },
  pending: { label: "En proceso", tone: "info", filter: "en_proceso" },
  processing: { label: "En proceso", tone: "info", filter: "en_proceso" },
};

/** Estados internos que cubre cada opción de filtro (CF-18: tres opciones, no cuatro). */
const FILTER_STATUSES: Record<StandaloneDocumentStatusFilter, StandaloneDocumentStatus[]> = {
  generated: ["generated"],
  error: ["error"],
  en_proceso: ["pending", "processing"],
};

export interface StandaloneDocumentStatusOption {
  value: StandaloneDocumentStatusFilter;
  label: string;
}

/** Opciones del selector de estado del historial — exactamente tres. */
export const STANDALONE_DOCUMENT_STATUS_OPTIONS: StandaloneDocumentStatusOption[] = [
  { value: "generated", label: STATUS_VIEW.generated.label },
  { value: "error", label: STATUS_VIEW.error.label },
  { value: "en_proceso", label: STATUS_VIEW.pending.label },
];

export function isStandaloneDocumentStatus(value: string): value is StandaloneDocumentStatus {
  return (STANDALONE_DOCUMENT_STATUSES as readonly string[]).includes(value);
}

/**
 * Traduce un estado del backend a su presentación. Un valor desconocido (contrato que se
 * amplía sin que el frontend se entere) se muestra como «En proceso» en tono neutro: no se
 * inventa una etiqueta nueva ni se afirma un resultado que no consta.
 */
export function standaloneDocumentStatusView(status: string): StandaloneDocumentStatusView {
  if (isStandaloneDocumentStatus(status)) {
    return STATUS_VIEW[status];
  }
  return { label: "En proceso", tone: "neutral", filter: "en_proceso" };
}

/** Etiqueta de usuario de un estado del backend. */
export function standaloneDocumentStatusLabel(status: string): string {
  return standaloneDocumentStatusView(status).label;
}

/** Estados internos que debe enviar el cliente al filtrar por una opción de usuario. */
export function standaloneDocumentStatusQuery(
  filter: StandaloneDocumentStatusFilter | "" | undefined,
): StandaloneDocumentStatus[] | undefined {
  if (!filter) {
    return undefined;
  }
  return FILTER_STATUSES[filter];
}

// ── Estados del LOTE (CF-21 en I3, HU #12211) ───────────────────────────────────────────────
//
// El lote tiene su propia máquina de cinco estados, distinta de la de los documentos. Sus
// etiquetas viven aquí, en el mismo y único archivo, por la misma razón: si mañana cambia
// «En proceso», cambia en un sitio y no en cinco componentes.

/** Los cinco estados internos de `admin.standalone_document_batches.status`. */
export const STANDALONE_BATCH_STATUSES = [
  "queued",
  "processing",
  "completed",
  "partial_failure",
  "failed",
] as const;

export type StandaloneBatchStatus = (typeof STANDALONE_BATCH_STATUSES)[number];

const BATCH_STATUS_VIEW: Record<StandaloneBatchStatus, { label: string; tone: StatusTone }> = {
  // `queued` y `processing` colapsan igual que `pending`/`processing` en un documento: para el
  // usuario «en cola» y «procesando» no son dos cosas distintas, son la misma espera.
  queued: { label: "En proceso", tone: "info" },
  processing: { label: "En proceso", tone: "info" },
  completed: { label: "Completado", tone: "success" },
  // `partial_failure` es un RESULTADO del lote, no un fallo: se acompaña siempre del conteo de
  // generados y de errores, que es lo que el AC exige mostrar.
  partial_failure: { label: "Completado con errores", tone: "warning" },
  failed: { label: "Sin documentos generados", tone: "danger" },
};

export function isStandaloneBatchStatus(value: string): value is StandaloneBatchStatus {
  return (STANDALONE_BATCH_STATUSES as readonly string[]).includes(value);
}

/**
 * Presentación de un estado de lote. Un valor desconocido se muestra como «En proceso» en tono
 * neutro: no se inventa una etiqueta ni se afirma un resultado que no consta.
 */
export function standaloneBatchStatusView(status: string): { label: string; tone: StatusTone } {
  if (isStandaloneBatchStatus(status)) {
    return BATCH_STATUS_VIEW[status];
  }
  return { label: "En proceso", tone: "neutral" };
}

/** Etiqueta de usuario de un estado de lote. */
export function standaloneBatchStatusLabel(status: string): string {
  return standaloneBatchStatusView(status).label;
}

/**
 * Estados terminales del lote. El backend además manda `isTerminal` explícito en la respuesta y
 * ESE es el que manda para detener el polling; esta lista es el respaldo cuando aún no hay
 * respuesta o cuando el contrato no trae la bandera.
 */
export function isStandaloneBatchTerminal(status: string | undefined): boolean {
  return status === "completed" || status === "partial_failure" || status === "failed";
}
