"use client";

/** Muestra los últimos 6 caracteres visibles de una URL (HU #10219 AC1). */
export function maskTargetUrl(url: string): string {
  if (url.length <= 6) {
    return url;
  }
  return `…${url.slice(-6)}`;
}

export const OT_WEBHOOK_EVENT_TYPES = [
  { value: "vehicle_state_changed", label: "Cambio estado vehículo" },
  { value: "procedure_state_changed", label: "Cambio estado trámite" },
] as const;

export const OT_PROCEDURE_STATUS_LABELS: Record<string, string> = {
  pending_ot: "Pendiente OT",
  approved_ot: "Aprobado OT",
  rejected_ot: "Rechazado OT",
};

export function formatOtProcedureStatus(status: string): string {
  return OT_PROCEDURE_STATUS_LABELS[status] ?? status;
}

export function procedureStatusTone(status: string): "success" | "warning" | "danger" | "neutral" {
  if (status === "approved_ot") return "success";
  if (status === "rejected_ot") return "danger";
  if (status === "pending_ot") return "warning";
  return "neutral";
}

export function formatOtDate(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return parsed.toLocaleDateString("es-CO", { year: "numeric", month: "2-digit", day: "2-digit" });
}
