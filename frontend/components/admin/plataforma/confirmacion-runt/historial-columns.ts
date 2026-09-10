import type { DataColumn } from "@/components/consultas/columns";
import {
  RUNT_VERDICT_LABEL,
  type RuntConfirmationAttemptRow,
  type RuntConfirmationVerdict,
} from "@/lib/api/admin-runt-confirmation";
import type { StatusTone } from "@/components/atom/StatusBadge";
import { bogotaClock } from "@/lib/xlsx";
import { formatFechaHora } from "@/lib/format/date";
import { PROVEEDOR_LABEL } from "./UltimaCorridaResumen";

/** Tono de la píldora por veredicto. El texto SIEMPRE acompaña al color (HU #12311 AC6). */
export const VERDICT_TONE: Record<RuntConfirmationVerdict, StatusTone> = {
  confirmed: "success",
  pending: "warning",
  discrepancy: "danger",
  unverifiable: "neutral",
  error: "info",
};

export const QUERY_KIND_LABEL: Record<string, string> = {
  vin: "Por VIN",
  plate: "Por placa + propietario",
  plate_pair: "Por placa: vendedor y comprador",
  vin_plate: "Por VIN + desempate por placa y propietario",
  reevaluation: "Re-evaluación desde el crudo",
};

export function vehiculoDe(row: Pick<RuntConfirmationAttemptRow, "plate" | "vin">): string {
  return row.plate ?? row.vin ?? "—";
}

export function proveedorLabel(key: string | null | undefined): string {
  return key ? (PROVEEDOR_LABEL[key] ?? key) : "—";
}

/**
 * Columnas del export XLSX del Historial (HU #12311 AC5): las mismas del listado más el motivo.
 * Comparte `DataColumn` con Consultas para reutilizar `buildWorkbook` y `exportarPorLotes`.
 */
export const HISTORIAL_EXPORT_COLUMNS: DataColumn<RuntConfirmationAttemptRow>[] = [
  { id: "fecha", label: "Fecha y hora", group: "Intento", value: (r) => formatFechaHora(r.queriedAt), raw: (r) => bogotaClock(r.queriedAt), width: 20 },
  { id: "tramite", label: "Trámite", group: "Trámite", value: (r) => r.referenceNumber, width: 12 },
  { id: "compania", label: "Compañía", group: "Trámite", value: (r) => r.tenantName ?? "—", raw: (r) => r.tenantName, width: 28 },
  { id: "tipo", label: "Tipo de trámite", group: "Trámite", value: (r) => r.procedureTypeName, width: 26 },
  { id: "placa", label: "Placa", group: "Vehículo", value: (r) => r.plate ?? "—", raw: (r) => r.plate, width: 10 },
  { id: "vin", label: "VIN", group: "Vehículo", value: (r) => r.vin ?? "—", raw: (r) => r.vin, width: 20 },
  { id: "proveedor", label: "Proveedor", group: "Intento", value: (r) => proveedorLabel(r.providerKey), width: 12 },
  { id: "consulta", label: "Consulta", group: "Intento", value: (r) => QUERY_KIND_LABEL[r.queryKind] ?? r.queryKind, width: 30 },
  { id: "intento", label: "N.º de intento", group: "Intento", value: (r) => String(r.attemptNo), raw: (r) => r.attemptNo, width: 14 },
  { id: "resultado", label: "Resultado", group: "Intento", value: (r) => RUNT_VERDICT_LABEL[r.verdict] ?? r.verdict, width: 18 },
  { id: "motivo", label: "Motivo", group: "Intento", value: (r) => r.reasonText, width: 80 },
  { id: "regla", label: "Versión de la regla", group: "Intento", value: (r) => r.ruleVersion, width: 18 },
  { id: "corrida", label: "Corrida", group: "Intento", value: (r) => r.runId ?? "—", raw: (r) => r.runId, width: 38 },
];

export const HISTORIAL_EXPORT_IDS = HISTORIAL_EXPORT_COLUMNS.map((c) => c.id);
