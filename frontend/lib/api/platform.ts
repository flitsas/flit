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

/** Un producto de la suite con su estado para una empresa (GET /api/v1/platform/admin/tenants/{tenantId}/products). */
export interface TenantProduct {
  productCode: string;
  name: string;
  enabled: boolean;
  notes: string | null;
  updatedAt: string | null;
  updatedBy: string | null;
  /** Correo de quien lo cambió por última vez, si se conoce. */
  updatedByEmail: string | null;
  /** Todavía no desplegado en este ambiente: se puede encender, pero sus usuarios verán «Próximamente». */
  comingSoon: boolean;
  /**
   * Solo en `diagnostico`: si la compañía tiene API key de impuesto guardada (`null` en los demás productos). Nunca trae
   * el valor, ni un prefijo ni una huella.
   */
  impuestoApiKeyConfigurada?: boolean | null;
  /**
   * HU #14711. Solo en `diagnostico`: si la compañía tiene secreto de webhook del proveedor de impuesto (`null` en los demás
   * productos). Nunca trae el valor.
   */
  impuestoWebhookSecretConfigurada?: boolean | null;
  /**
   * HU #14694/#14695 (platform.v1.yaml 1.1.0). Estado de aprovisionamiento que reporta el servicio del producto. `null` en
   * los productos sin aprovisionamiento (hoy solo `impuestos` lo tiene) y mientras no llega el primer reporte.
   */
  provisioning?: TenantProductProvisioning | null;
}

// ── Aprovisionamiento (HU #14695 · M19; contrato platform.v1.yaml 1.1.0, plan §3.1) ─────────────────────────────

/** Estados del plan §3.1 (`ProvisioningStatus`). */
export type ProvisioningStatus =
  | "PENDIENTE"
  | "LISTO"
  | "LISTO_SIN_WEBHOOK"
  | "ERROR"
  | "INCIERTO"
  | "REQUIERE_CREDENCIALES"
  | "REQUIERE_REVISION_NIT"
  | "SUSPENDIDO";

/**
 * Acción del SuperAdmin por estado (`ProvisioningAction`). Desde impuestos.v1.yaml 1.2.0 (plan §5.8) LISTO_SIN_WEBHOOK
 * lleva REINTENTAR (el alta idempotente trae el secreto) y CARGAR_SECRETO queda retirada: el literal se conserva.
 */
export type ProvisioningAction = "REINTENTAR" | "CARGAR_SECRETO" | "CARGAR_CREDENCIALES" | "CORREGIR_NIT";

/**
 * Acción que se ofrece hoy. CARGAR_SECRETO (retirada en 1.2.0) solo puede llegar de un reporte anterior al cambio, que
 * la plataforma conserva hasta el siguiente: se ofrece «Reintentar», como en el backoffice de Impuestos.
 */
export function accionVigente(action: ProvisioningAction | null | undefined): ProvisioningAction | null {
  if (!action) return null;
  return action === "CARGAR_SECRETO" ? "REINTENTAR" : action;
}

/** Último estado reportado (`TenantProductProvisioning`). `message` es el motivo legible, ya puesto por el servicio. */
export interface TenantProductProvisioning {
  status: ProvisioningStatus;
  reason: string | null;
  message: string | null;
  action: ProvisioningAction | null;
  /** Solo en LISTO: `false` = «Firma sin confirmar». `null` en los demás estados. */
  signatureConfirmed: boolean | null;
  updatedAt: string;
}

export type NitStatus = "VALIDO" | "AMBIGUO" | "AUSENTE" | "IGUAL_A_CABEZA";

/** Datos que se enviarán al proveedor en el alta (`ProvisioningPreview`, GET …/alta-previa). */
export interface ProvisioningPreview {
  legalName: string;
  /** NIT base sin dígito de verificación (D-TAXID). `null` si `nitStatus` no es VALIDO. */
  taxIdBase: string | null;
  nitStatus: NitStatus;
  /** Correo del AdminCompany activo más antiguo (D-EMAIL). `null` si no hay ninguno. */
  contactEmail: string | null;
  blocking: "SIN_ADMIN" | null;
}

// Textos visibles cerrados por el contrato (`x-flit-etiquetas`): el front no redacta otros.
export const ETIQUETA_ESTADO_APROVISIONAMIENTO: Record<ProvisioningStatus, string> = {
  PENDIENTE: "Pendiente",
  LISTO: "Listo",
  LISTO_SIN_WEBHOOK: "Listo sin webhook",
  ERROR: "Error",
  INCIERTO: "Incierto",
  REQUIERE_CREDENCIALES: "Requiere credenciales",
  REQUIERE_REVISION_NIT: "Requiere revisión del NIT",
  SUSPENDIDO: "Suspendido",
};

export const ETIQUETA_ACCION_APROVISIONAMIENTO: Record<ProvisioningAction, string> = {
  REINTENTAR: "Reintentar",
  CARGAR_SECRETO: "Cargar secreto",
  CARGAR_CREDENCIALES: "Cargar credenciales",
  CORREGIR_NIT: "Corregir NIT",
};

export const ETIQUETA_FIRMA_SIN_CONFIRMAR = "Firma sin confirmar";

export const ETIQUETA_ESTADO_NIT: Record<NitStatus, string> = {
  VALIDO: "NIT válido",
  AMBIGUO: "No se puede separar el dígito de verificación del NIT",
  AUSENTE: "La compañía no tiene NIT",
  IGUAL_A_CABEZA: "El NIT es el mismo de su compañía cabeza",
};

/** `blocking` = SIN_ADMIN; también es el `detail` del 409 PROVISIONING_PREREQUISITE_MISSING. */
export const ETIQUETA_SIN_ADMIN = "La compañía no tiene un Administrador de Compañía activo";
export const CODIGO_PREREQUISITO_FALTANTE = "PROVISIONING_PREREQUISITE_MISSING";

/** GET /api/v1/platform/admin/tenants/{tenantId}/products — solo SuperAdmin. */
export function listTenantProducts(tenantId: string): Promise<TenantProduct[]> {
  return apiFetch<TenantProduct[]>(`/api/v1/platform/admin/tenants/${encodeURIComponent(tenantId)}/products`);
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

const credentialPath = (tenantId: string, productCode: string, credentialCode: string) =>
  `/api/v1/platform/admin/tenants/${encodeURIComponent(tenantId)}/products/${encodeURIComponent(productCode)}` +
  `/credentials/${encodeURIComponent(credentialCode)}`;

/**
 * PUT /api/v1/platform/admin/tenants/{tenantId}/products/{productCode}/credentials/{credentialCode} — solo SuperAdmin.
 * Crea o reemplaza (204). El valor solo viaja en el cuerpo; nunca se guarda ni se devuelve.
 */
export function setTenantProductCredential(
  tenantId: string,
  productCode: string,
  credentialCode: string,
  value: string,
): Promise<void> {
  return apiFetch<void>(credentialPath(tenantId, productCode, credentialCode), { method: "PUT", body: { value } });
}

/** DELETE de la misma ruta — solo SuperAdmin. Idempotente (204). */
export function deleteTenantProductCredential(tenantId: string, productCode: string, credentialCode: string): Promise<void> {
  return apiFetch<void>(credentialPath(tenantId, productCode, credentialCode), { method: "DELETE" });
}

/**
 * Resultado del alta idempotente con el proveedor de impuesto (HU #14711). Solo metadatos: nunca la llave ni el secreto.
 */
export interface CredencialImpuestoObtenida {
  apiKeyPrefix: string;
  created: boolean;
  rotated: boolean;
  hasWebhookSecret: boolean;
  changed: boolean;
  updatedAt: string;
}

/**
 * POST /api/v1/platform/admin/tenants/{tenantId}/products/diagnostico/credentials/impuesto/obtener — solo SuperAdmin, sin
 * cuerpo (el servidor arma el alta con el NIT de la empresa). Se copian solo los campos del contrato, para que un campo de
 * más en la respuesta no llegue al estado de la UI.
 */
export async function obtenerCredencialImpuestoDiagnostico(tenantId: string): Promise<CredencialImpuestoObtenida> {
  const r = await apiFetch<CredencialImpuestoObtenida>(
    `/api/v1/platform/admin/tenants/${encodeURIComponent(tenantId)}/products/diagnostico/credentials/impuesto/obtener`,
    { method: "POST" },
  );
  return {
    apiKeyPrefix: r.apiKeyPrefix,
    created: r.created,
    rotated: r.rotated,
    hasWebhookSecret: r.hasWebhookSecret,
    changed: r.changed,
    updatedAt: r.updatedAt,
  };
}

const productPath = (tenantId: string, productCode: string) =>
  `/api/v1/platform/admin/tenants/${encodeURIComponent(tenantId)}/products/${encodeURIComponent(productCode)}`;

/** GET /api/v1/platform/admin/tenants/{tenantId}/products/{productCode}/alta-previa — solo SuperAdmin, solo lectura. */
export function getTenantProductProvisioningPreview(tenantId: string, productCode: string): Promise<ProvisioningPreview> {
  return apiFetch<ProvisioningPreview>(`${productPath(tenantId, productCode)}/alta-previa`);
}

/** POST …/products/{productCode}/aprovisionamiento/reintentar — solo SuperAdmin. Sin cuerpo; 202 = trabajo encolado. */
export function retryTenantProductProvisioning(tenantId: string, productCode: string): Promise<void> {
  return apiFetch<void>(`${productPath(tenantId, productCode)}/aprovisionamiento/reintentar`, { method: "POST" });
}
