import { API_BASE_URL, apiFetch, friendlyErrorMessage, getToken } from "./client";
import { ApiError } from "./types";

const base = "/api/v1/admin/plataforma/fur";

export type FurPersonKind = "natural" | "juridica";
export type FurVehicleKind = "carro" | "moto" | "camioneta" | "remolque" | "maquinaria";
export type FurPrendaKind = "ninguna" | "inscripcion" | "levantamiento" | "ambas";
export type FurTemplateFormatCode = "AUTOMOTOR" | "MAQUINARIA" | "REMOLQUES";

export interface FurPreviewRequest {
  procedureTypeId?: string;
  sellerPersonKind?: FurPersonKind;
  buyerPersonKind?: FurPersonKind;
  vehicleKind?: FurVehicleKind;
  vehicleClass?: string;
  templateFormat?: FurTemplateFormatCode;
  cambioColor?: boolean;
  cambioCombustible?: boolean;
  cambioCarroceria?: boolean;
  blindaje?: boolean;
  prenda?: FurPrendaKind;
  fillAll?: boolean;
}

export interface FurClassificationItem {
  classification: string;
  templateFormat: FurTemplateFormatCode | string;
  fieldToFill: string | null;
}

export async function listFurClassifications(signal?: AbortSignal): Promise<FurClassificationItem[]> {
  const data = await apiFetch<{ items: FurClassificationItem[] } | FurClassificationItem[]>(
    `${base}/classifications`,
    { signal },
  );
  const rows = Array.isArray(data) ? data : (data?.items ?? []);
  return rows.map((r) => {
    const raw = r as unknown as Record<string, unknown>;
    return {
      classification: String(raw.classification ?? raw.Classification ?? ""),
      templateFormat: String(raw.templateFormat ?? raw.TemplateFormat ?? "").toUpperCase(),
      fieldToFill: (raw.fieldToFill ?? raw.FieldToFill ?? null) as string | null,
    };
  });
}

export async function fetchFurPreview(body: FurPreviewRequest, signal?: AbortSignal): Promise<Blob> {
  const baseUrl =
    API_BASE_URL || (typeof window !== "undefined" ? window.location.origin : "http://localhost:3000");
  const url = new URL(`${base}/preview`, baseUrl);
  const token = getToken();
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (token) headers.Authorization = `Bearer ${token}`;

  const response = await fetch(url.toString(), {
    method: "POST",
    headers,
    body: JSON.stringify(body),
    signal,
  });
  if (!response.ok) {
    let detail: unknown = null;
    try {
      const text = await response.text();
      detail = text ? JSON.parse(text) : null;
    } catch {
      /* ignore */
    }
    throw new ApiError(response.status, friendlyErrorMessage(detail as Record<string, unknown> | null), detail);
  }

  const blob = await response.blob();
  return blob.type === "application/pdf" ? blob : new Blob([blob], { type: "application/pdf" });
}
