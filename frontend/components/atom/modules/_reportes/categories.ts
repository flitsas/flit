// Metadatos de presentación para el dashboard analítico (HU #10247). Centraliza los
// colores y etiquetas para que ningún componente use HEX sueltos: la paleta es la
// misma que el resto del shell FLIT (Dashboard, UiStateBoundary).
import type { AnalyticsCategory } from "@/lib/api/types";

/** Orden estable en el que se pintan las categorías (RF01). "otros" cierra como catch-all. */
export const CATEGORY_ORDER: readonly AnalyticsCategory[] = ["matriculas", "traspasos", "vehicular", "otros"];

/** Etiqueta legible + color de marca por categoría de trámite. */
export const CATEGORY_META: Record<AnalyticsCategory, { label: string; color: string }> = {
  matriculas: { label: "Matrículas", color: "#557EFF" },
  traspasos: { label: "Traspasos", color: "#00DBD5" },
  vehicular: { label: "Vehicular", color: "#9B8AFB" },
  otros: { label: "Otros", color: "#F9AC00" },
};

const STATUS_LABELS: Record<string, string> = {
  draft: "Borrador",
  submitted: "Radicado",
  in_review: "En revisión",
  pending: "Pendiente",
  approved: "Aprobado",
  completed: "Completado",
  rejected: "Rechazado",
  cancelled: "Cancelado",
};

/** Nombre legible de un estado; capitaliza y normaliza desconocidos sin romper la UI. */
export function statusLabel(status: string): string {
  return STATUS_LABELS[status] ?? status.charAt(0).toUpperCase() + status.slice(1).replace(/_/g, " ");
}

const STATUS_COLORS: Record<string, string> = {
  draft: "#59677D",
  submitted: "#557EFF",
  in_review: "#F9AC00",
  pending: "#F9AC00",
  approved: "#8CC63F",
  completed: "#00DBD5",
  rejected: "#FF4E00",
  cancelled: "#E43D30",
};

const FALLBACK_PALETTE = ["#557EFF", "#00DBD5", "#F9AC00", "#8CC63F", "#FF4E00", "#9B8AFB", "#59677D"];

/** Color estable de un estado; los desconocidos ciclan una paleta por índice. */
export function statusColor(status: string, index = 0): string {
  return STATUS_COLORS[status] ?? FALLBACK_PALETTE[index % FALLBACK_PALETTE.length];
}
