// Cliente de Administración → Plataforma → Contingencia de proveedor RUNT (Épica #14644, Feature #14646).
// Contrato: docs/design/EPICA-14644-contrato.md (`/api/v1/admin/platform/runt-contingency`).
import { apiFetch } from "./client";
import { ApiError } from "./types";

const base = "/api/v1/admin/platform/runt-contingency";

export type RuntContingencyKind = "vehicle_vin" | "vehicle_plate" | "conductor";
export type RuntContingencyProvider =
  | "kyverum_runt"
  | "verifik"
  | "kyverum_runt_conductor"
  | "verifik_conductor";

/** Máximo de caracteres del motivo (el backend lo recorta y valida de 1 a 500). */
export const RUNT_CONTINGENCY_REASON_MAX = 500;

export const RUNT_CONTINGENCY_KIND_LABEL: Record<RuntContingencyKind, string> = {
  vehicle_vin: "Vehículo por VIN",
  vehicle_plate: "Vehículo por placa",
  conductor: "Conductor",
};

export const RUNT_CONTINGENCY_PROVIDER_LABEL: Record<RuntContingencyProvider, string> = {
  kyverum_runt: "Kyverum",
  verifik: "Verifik",
  kyverum_runt_conductor: "Kyverum",
  verifik_conductor: "Verifik",
};

/** Proveedores que atiende cada tipo de consulta (vehículo ≠ conductor). */
export const RUNT_CONTINGENCY_PROVIDERS_BY_KIND: Record<RuntContingencyKind, RuntContingencyProvider[]> = {
  vehicle_vin: ["kyverum_runt", "verifik"],
  vehicle_plate: ["kyverum_runt", "verifik"],
  conductor: ["kyverum_runt_conductor", "verifik_conductor"],
};

export interface RuntContingencyItem {
  kind: RuntContingencyKind;
  /** `null` = «Sin contingencia». */
  provider: RuntContingencyProvider | null;
  reason: string | null;
  updatedByName: string | null;
  updatedAt: string | null;
}

export interface RuntContingencyResponse {
  items: RuntContingencyItem[];
}

export interface RuntContingencyInput {
  provider: RuntContingencyProvider | null;
  reason: string;
}

export async function getRuntContingency(signal?: AbortSignal): Promise<RuntContingencyResponse> {
  return apiFetch<RuntContingencyResponse>(base, { signal });
}

export async function putRuntContingency(
  kind: RuntContingencyKind,
  input: RuntContingencyInput,
): Promise<RuntContingencyItem> {
  return apiFetch<RuntContingencyItem>(`${base}/${kind}`, { method: "PUT", body: input });
}

/**
 * Motivo exacto de un fallo para mostrarlo tal cual: en un 400 (ValidationProblemDetails) los mensajes de
 * errors.reason / errors.provider; en un 503 el detail, que apiFetch ya deja en message.
 */
export function runtContingencyErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof ApiError && err.status === 400) {
    const errors = (err.body as { errors?: Record<string, unknown> } | undefined)?.errors;
    if (errors && typeof errors === "object") {
      const mensajes = Object.values(errors)
        .flatMap((v) => (Array.isArray(v) ? v : [v]))
        .filter((m): m is string => typeof m === "string" && m.trim() !== "");
      if (mensajes.length > 0) return mensajes.join(" ");
    }
  }
  return err instanceof Error && err.message.trim() ? err.message : fallback;
}
