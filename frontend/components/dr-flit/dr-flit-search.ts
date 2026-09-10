import { fetchOtClientProcedures } from "@/lib/api/admin-ot";
import { tramitesClient } from "@/lib/api/tramites-client";
import type { DrFlitIntentId } from "./dr-flit-intents";
import {
  mapBiometricToResult,
  mapInstanceSummaryToResult,
  mapOtProcedureToResult,
} from "./dr-flit-mappers";
import type { DrFlitTramiteResult, DrFlitValidacionResult } from "./dr-flit-types";

const GUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export type DrFlitSearchRole = {
  isOtAdmin: boolean;
};

export function isGuid(value: string): boolean {
  return GUID_RE.test(value.trim());
}

function looksLikeDocument(value: string): boolean {
  const compact = value.replace(/[\s.-]/g, "");
  return /^\d{5,}$/.test(compact);
}

/** Busca trámites por placa, VIN, ID o texto de cliente (comprador/vendedor). */
export async function searchTramites(
  intent: Exclude<DrFlitIntentId, "cliente"> | "cliente",
  rawValue: string,
  role: DrFlitSearchRole,
): Promise<DrFlitTramiteResult[]> {
  const value = rawValue.trim();
  if (!value) return [];

  if (intent === "tramite") {
    if (!isGuid(value)) {
      throw new Error(
        "Indica el ID del trámite (GUID). Ejemplo: 3fa85f64-5717-4562-b3fc-2c963f66afa6",
      );
    }
    const detail = await tramitesClient.getInstance(value);
    const field = (key: string) =>
      detail.fieldValues?.find(
        (f) => f.fieldKey?.toLowerCase() === key || f.fieldKey?.toLowerCase().endsWith(`.${key}`),
      )?.valueText ?? null;
    return [
      {
        id: detail.id,
        fecha: (detail.createdAt ?? "").slice(0, 10) || "—",
        estado: detail.status,
        placa: (field("placa") ?? "—").toUpperCase(),
        vin: field("vin") ?? "—",
        tipoTramite: detail.referenceNumber
          ? `Trámite ${detail.referenceNumber}`
          : "Trámite",
        href: `/tramites/${detail.id}`,
      },
    ];
  }

  if (role.isOtAdmin && (intent === "placa" || intent === "vin")) {
    const page = await fetchOtClientProcedures({
      placa: intent === "placa" ? value.toUpperCase() : undefined,
      vin: intent === "vin" ? value.toUpperCase() : undefined,
      page: 1,
      pageSize: 20,
    });
    return (page.data ?? []).map(mapOtProcedureToResult);
  }

  if (intent === "placa" || intent === "vin") {
    const items = await tramitesClient.listInstances({
      placa: intent === "placa" ? value.toUpperCase() : undefined,
      vin: intent === "vin" ? value.toUpperCase() : undefined,
      take: 50,
      skip: 0,
    });
    return items.map(mapInstanceSummaryToResult);
  }

  // cliente → búsqueda por comprador y vendedor (substring), dedupe por id
  const [byComprador, byVendedor] = await Promise.all([
    tramitesClient.listInstances({ comprador: value, take: 50, skip: 0 }),
    tramitesClient.listInstances({ vendedor: value, take: 50, skip: 0 }),
  ]);
  const map = new Map<string, DrFlitTramiteResult>();
  for (const item of [...byComprador, ...byVendedor]) {
    map.set(item.id, mapInstanceSummaryToResult(item));
  }

  if (role.isOtAdmin) {
    const ot = await fetchOtClientProcedures({
      comprador: value,
      page: 1,
      pageSize: 20,
    });
    const otVend = await fetchOtClientProcedures({
      vendedor: value,
      page: 1,
      pageSize: 20,
    });
    for (const item of [...(ot.data ?? []), ...(otVend.data ?? [])]) {
      map.set(item.id, mapOtProcedureToResult(item));
    }
  }

  return Array.from(map.values());
}

/** Busca validaciones de identidad por documento o nombre. */
export async function searchValidaciones(
  rawValue: string,
): Promise<DrFlitValidacionResult[]> {
  const value = rawValue.trim();
  if (!value) return [];

  const filters = looksLikeDocument(value)
    ? { documentNumber: value.replace(/[\s.-]/g, ""), page: 1, pageSize: 20 }
    : { name: value, page: 1, pageSize: 20 };

  const res = await tramitesClient.listTenantBiometricValidations(filters);
  return (res.validations ?? []).map(mapBiometricToResult);
}
