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
