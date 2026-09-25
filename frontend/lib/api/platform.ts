// Cliente de la API de plataforma de la FLIT Suite (contrato de plataforma v1, §6; HU #12966 y #12967).
import { apiFetch } from "./client";

/** Producto que el usuario puede abrir (GET /api/v1/platform/me/apps). */
export interface MyApp {
  code: string;
  name: string;
  icon: string;
  url: string;
  current: boolean;
}

/** Estado de un producto para una empresa, tras encenderlo o apagarlo. */
export interface TenantProductState {
  productCode: string;
  enabled: boolean;
  notes: string | null;
  updatedAt: string | null;
  updatedBy: string | null;
  changed: boolean;
}

export function listMyApps(): Promise<MyApp[]> {
  return apiFetch<MyApp[]>("/api/v1/platform/me/apps");
}

/** PUT /api/v1/platform/admin/tenants/{tenantId}/products/{productCode} — solo SuperAdmin. */
export function setTenantProduct(
  tenantId: string,
  productCode: string,
  enabled: boolean,
  notes?: string | null,
): Promise<TenantProductState> {
  return apiFetch<TenantProductState>(
    `/api/v1/platform/admin/tenants/${encodeURIComponent(tenantId)}/products/${encodeURIComponent(productCode)}`,
    { method: "PUT", body: { enabled, notes: notes ?? null } },
  );
}
