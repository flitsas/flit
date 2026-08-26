import type { InstanceSummary } from "@/lib/api/types/procedure-runtime";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import type { TenantBiometricValidation } from "@/lib/api/types/procedure-runtime";
import type { DrFlitTramiteResult, DrFlitValidacionResult } from "./dr-flit-types";

function modalidadLabel(modalidad: string | null | undefined): string {
  if (modalidad === "matricula_inicial") return "Matrícula inicial";
  if (modalidad === "traspaso") return "Traspaso";
  return modalidad?.replace(/_/g, " ") || "Trámite";
}

function dateOnly(iso: string | null | undefined): string {
  if (!iso) return "—";
  return iso.slice(0, 10);
}

export function mapInstanceSummaryToResult(item: InstanceSummary): DrFlitTramiteResult {
  return {
    id: item.id,
    fecha: dateOnly(item.createdAt),
    estado: item.estado,
    placa: (item.placa ?? "—").toUpperCase(),
    vin: item.vin ?? "—",
    tipoTramite: modalidadLabel(item.modalidad),
    href: `/tramites/${item.id}`,
  };
}

export function mapOtProcedureToResult(item: OtClientProcedure): DrFlitTramiteResult {
  return {
    id: item.id,
    fecha: dateOnly(item.createdAt),
    estado: item.status,
    placa: (item.placa ?? "—").toUpperCase(),
    vin: item.vin ?? "—",
    tipoTramite: item.procedureTypeName || "Trámite",
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
    createdAt: dateOnly(item.createdAt),
    instanceId: item.instanceId,
    href: `/?m=validaciones&q=${q}`,
    tramiteHref: item.instanceId ? `/tramites/${item.instanceId}` : null,
  };
}
