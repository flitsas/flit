// Muestra de correo con el tema de la red — HU #12431 (AC1). Autogestión de la cabeza
// (`GET /api/v1/company/branding/email-sample`, contrato en
// `.claude/state/marca-blanca/diseno/contratos-api.md` § 4). Separado de `lib/api/branding.ts`
// (identidad de marca: borrador/publicación/logo) para aislar el acceso a este único endpoint
// nuevo — cualquier ajuste de contrato queda local a este archivo.
import { apiFetch } from "./client";
import type { EmailThemeInfo } from "./types";

export type CompanyBrandingEmailSampleSource = "draft" | "published";

export interface CompanyBrandingEmailSampleResponse {
  templateId: string;
  subject: string;
  html: string;
  /**
   * Aditivo. `kind: "draft-partial"` = el borrador está incompleto y se completó con FLIT
   * (`source=draft`). `kind: "flit"` con `source=published` = no hay marca publicada todavía.
   */
  theme?: EmailThemeInfo;
}

const emailSampleBase = "/api/v1/company/branding/email-sample";

/**
 * `GET /api/v1/company/branding/email-sample?templateId=&source=draft|published`.
 * `templateId` por defecto `tramites.aprobado` (mismo default del backend) si no se indica.
 * `400 BRANDING_SAMPLE_TEMPLATE_NOT_ALLOWED` — plantilla fuera de `Branding:SampleTemplates`.
 * `404 BRANDING_NOT_FOUND` — la cabeza aún no hizo la configuración inicial de marca.
 * `403` — hija, Concesión o compañía sin red (esta ruta es solo para la cabeza autenticada).
 */
export async function getCompanyBrandingEmailSample(
  options: { templateId?: string; source: CompanyBrandingEmailSampleSource },
  signal?: AbortSignal,
): Promise<CompanyBrandingEmailSampleResponse> {
  const params = new URLSearchParams();
  if (options.templateId) params.set("templateId", options.templateId);
  params.set("source", options.source);
  return apiFetch<CompanyBrandingEmailSampleResponse>(
    `${emailSampleBase}?${params.toString()}`,
    { signal },
  );
}
