// Metadatos de presentación para el dashboard analítico (HU #10247). Centraliza los
// colores y etiquetas para que ningún componente use HEX sueltos: la paleta es la
// misma que el resto del shell FLIT (Dashboard, UiStateBoundary).
import type { AnalyticsCategory } from "@/lib/api/types";
import type { ProcedureFamily } from "@/lib/api/types/procedure-parametrization";

/** Orden estable en el que se pintan las categorías (RF01). "otros" cierra como catch-all. */
export const CATEGORY_ORDER: readonly AnalyticsCategory[] = ["matriculas", "traspasos", "otros"];

/** Familia del tipo de trámite detrás de cada categoría (HU #13665: Ver por familia oculta su tarjeta y su serie). */
export const CATEGORY_FAMILY: Record<AnalyticsCategory, ProcedureFamily> = {
  matriculas: "MATRICULAS",
  traspasos: "TRASPASO",
  otros: "OTROS",
};

/** Etiqueta legible + color de marca por categoría de trámite. */
export const CATEGORY_META: Record<AnalyticsCategory, { label: string; color: string }> = {
  matriculas: { label: "Matrículas", color: "var(--color-flit-brand)" },
  traspasos: { label: "Traspasos", color: "var(--color-flit-tech)" },
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
  // Estados N03 (ya en español; se capitalizan explícitamente para estabilidad).
  borrador: "Borrador",
  anulado: "Anulado",
  preparado: "Preparado",
  entregado: "Entregado",
  aprobado: "Aprobado",
  rechazado: "Rechazado",
};

/** Nombre legible de un estado; capitaliza y normaliza desconocidos sin romper la UI. */
export function statusLabel(status: string): string {
  return STATUS_LABELS[status] ?? status.charAt(0).toUpperCase() + status.slice(1).replace(/_/g, " ");
}

const STATUS_COLORS: Record<string, string> = {
  draft: "#59677D",
  submitted: "var(--color-flit-brand)",
  in_review: "#F9AC00",
  pending: "#F9AC00",
  approved: "#8CC63F",
  completed: "var(--color-flit-tech)",
  rejected: "#FF4E00",
  cancelled: "#E43D30",
  // Estados N03 (mismos semáforos que sus equivalentes legados).
  borrador: "#59677D",
  anulado: "#E43D30",
  preparado: "#F9AC00",
  entregado: "var(--color-flit-brand)",
  aprobado: "#8CC63F",
  rechazado: "#FF4E00",
};

const FALLBACK_PALETTE = ["var(--color-flit-brand)", "var(--color-flit-tech)", "#F9AC00", "#8CC63F", "#FF4E00", "#9B8AFB", "#59677D"];

/** Color estable de un estado; los desconocidos ciclan una paleta por índice. */
export function statusColor(status: string, index = 0): string {
  return STATUS_COLORS[status] ?? FALLBACK_PALETTE[index % FALLBACK_PALETTE.length];
}
