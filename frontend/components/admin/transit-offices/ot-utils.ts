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
};

export function formatOtProcedureStatus(status: string): string {
  return OT_PROCEDURE_STATUS_LABELS[status] ?? status;
}

export function procedureStatusTone(status: string): "success" | "warning" | "danger" | "neutral" {
  if (status === "aprobado") return "success";
  if (status === "rechazado") return "danger";
  if (status === "entregado") return "warning";
  return "neutral";
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
