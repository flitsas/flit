"use client";

import { formatFecha } from "@/lib/format/date";

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

// N 03 (ADR-0022) — vocabulario de estados de negocio: `entregado` = en cola de decisión OT.
export const OT_PROCEDURE_STATUS_LABELS: Record<string, string> = {
  entregado: "Pendiente OT",
  aprobado: "Aprobado OT",
  rechazado: "Rechazado OT",
  // HU #12166 (Feature #12156) — el OT deshizo su propia aprobación.
  revocado: "Revocado OT",
};

export function formatOtProcedureStatus(status: string): string {
  return OT_PROCEDURE_STATUS_LABELS[status] ?? status;
}

export function procedureStatusTone(status: string): "success" | "warning" | "danger" | "neutral" {
  if (status === "aprobado") return "success";
  if (status === "rechazado" || status === "revocado") return "danger";
  if (status === "entregado") return "warning";
  return "neutral";
}

/**
 * HU #12167 (Feature #12156) — estado de la ventana de 1 hora para corregir la placa asignada por
 * el OT. `disabled` cubre las dos causas de AC2/AC3 (ventana cerrada o ya usada); `disabledReason`
 * es el texto que ve el OT en el ítem del menú.
 */
export function plateUpdateWindow(
  plateAssignedAt: string,
  plateUpdatedAt: string | null | undefined,
): { disabled: boolean; disabledReason?: string; minutosRestantes: number } {
  if (plateUpdatedAt) {
    return {
      disabled: true,
      disabledReason: "Ya se usó la única corrección de placa permitida para este trámite.",
      minutosRestantes: 0,
    };
  }

  const assignedMs = new Date(plateAssignedAt).getTime();
  const elapsedMinutes = (Date.now() - assignedMs) / 60_000;
  const minutosRestantes = Math.max(0, Math.ceil(60 - elapsedMinutes));

  if (minutosRestantes <= 0) {
    return {
      disabled: true,
      disabledReason: "Pasó más de 1 hora desde la asignación: ya no se puede corregir por este medio.",
      minutosRestantes: 0,
    };
  }

  return { disabled: false, minutosRestantes };
}

/**
 * HU #12167/#12168 — tiempo restante en formato MM:SS para el contador EN VIVO del modal de
 * corrección de placa (a diferencia de `plateUpdateWindow`, que redondea a minutos para el ítem
 * del menú). "00:00" cuando ya venció.
 */
export function plateUpdateRemainingLabel(plateAssignedAt: string): string {
  const deadlineMs = new Date(plateAssignedAt).getTime() + 60 * 60_000;
  const remainingMs = Math.max(0, deadlineMs - Date.now());
  const totalSeconds = Math.floor(remainingMs / 1000);
  const mm = Math.floor(totalSeconds / 60);
  const ss = totalSeconds % 60;
  return `${String(mm).padStart(2, "0")}:${String(ss).padStart(2, "0")}`;
}

// HU #11018 — formato de negocio unico: AÑO/MES/DIA, sin hora.
export function formatOtDate(iso: string): string {
  return formatFecha(iso, iso);
}

export const OT_RULE_FIELDS = [
  { value: "deuda_pendiente", label: "Deuda pendiente" },
  { value: "tipo_tramite", label: "Tipo de trámite" },
  { value: "prioridad", label: "Prioridad" },
] as const;

export const OT_RULE_OPERATORS = [
  { value: "eq", label: "Igual a" },
  { value: "in", label: "Está en" },
] as const;

export const OT_RULE_ACTIONS = [
  { value: "bloquear", label: "Bloquear" },
  { value: "biometria", label: "Biometría" },
  { value: "cola_especial", label: "Cola especial" },
] as const;

export function formatOtRuleAction(type: string): string {
  return OT_RULE_ACTIONS.find((a) => a.value === type)?.label ?? type;
}
