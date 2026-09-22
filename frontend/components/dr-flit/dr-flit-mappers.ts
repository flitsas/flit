import type { InstanceSummary } from "@/lib/api/types/procedure-runtime";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import type { TenantBiometricValidation } from "@/lib/api/types/procedure-runtime";
import { formatFechaHora } from "@/lib/format/date";
import type { NetworkInstanceSummary } from "@/lib/tramites/network-scope";
import { tramiteLabel } from "@/lib/tramites/tramites-row-labels";
import type { DrFlitTramiteResult, DrFlitValidacionResult } from "./dr-flit-types";

/** Épica #12552 (RN-02): la radicación es un instante → fecha y hora en hora de Colombia. */
export function fechaRadicacion(iso: string | null | undefined): string {
  return formatFechaHora(iso, "—");
}

function nombreCompania(value: string | null | undefined): string | null {
  const v = value?.trim();
  return v ? v : null;
}

export function mapInstanceSummaryToResult(
  item: InstanceSummary,
  /** `true` cuando la lista puede mezclar compañías (SuperAdmin): se conserva `companiaNombre`. */
  withCompania = false,
): DrFlitTramiteResult {
  return {
    id: item.id,
    radicado: item.referenceNumber || "—",
    fecha: fechaRadicacion(item.createdAt),
    estado: item.estado,
    placa: (item.placa ?? "—").toUpperCase(),
    vin: item.vin ?? "—",
    // HU #12181 — mismo rótulo que la fila del listado: el nombre del TIPO, respaldo a la familia.
    tipoTramite: tramiteLabel(item),
    compania: withCompania ? nombreCompania(item.companiaNombre) : null,
    href: `/tramites/${item.id}`,
  };
}

/** Fila de `network/**`: la compañía dueña viene en `tenantName` (HU #12362). */
export function mapNetworkInstanceToResult(item: NetworkInstanceSummary): DrFlitTramiteResult {
  return {
    ...mapInstanceSummaryToResult(item, false),
    compania: nombreCompania(item.tenantName) ?? nombreCompania(item.companiaNombre),
  };
}

export function mapOtProcedureToResult(item: OtClientProcedure): DrFlitTramiteResult {
  return {
    id: item.id,
    radicado: item.referenceNumber || "—",
    fecha: fechaRadicacion(item.createdAt),
    estado: item.status,
    placa: (item.placa ?? "—").toUpperCase(),
    vin: item.vin ?? "—",
    tipoTramite: item.procedureTypeName || "Trámite",
    compania: nombreCompania(item.clientTenantName),
    href: `/tramites/${item.id}`,
  };
}

export function mapBiometricToResult(item: TenantBiometricValidation): DrFlitValidacionResult {
  const q = encodeURIComponent(item.documentNumber || item.name);
  return {
    id: item.id,
    name: item.name,
    documentType: item.documentType,
    documentNumber: item.documentNumber,
    status: item.status,
    createdAt: formatFechaHora(item.createdAt, "—"),
    instanceId: item.instanceId,
    href: `/?m=validaciones&q=${q}`,
    tramiteHref: item.instanceId ? `/tramites/${item.instanceId}` : null,
  };
}
