// Cliente SuperAdmin — Plataforma → Mandatos (config por OT + plantilla propia + preview + extract).
import { API_BASE_URL, apiFetch, friendlyErrorMessage, getToken } from "./client";
import { ApiError } from "./types";
import type { MandateAssignmentMode } from "@/lib/plataforma/mandato-templates";

const base = "/api/v1/admin/plataforma/mandatos";

export type MandataryFamilyCode = "individuo" | "organismo_transito";
export type CustomTemplateKind = "none" | "pdf" | "editor";

export interface MandateOtConfigView {
  officeId: string;
  code: string;
  name: string;
  /** Redacción EFECTIVA que se emite hoy para este OT (con `auto` ya resuelto). */
  templateCode: string;
  /**
   * Redacción ELEGIDA, tal cual está guardada (`auto` si el OT no fija ninguna). Es la que
   * preselecciona el selector: preseleccionar con la efectiva convertiría un "automática" en una
   * redacción fija al guardar sin tocar nada.
   */
  configuredTemplateCode: string;
  requiresForNaturalPerson: boolean;
  mandataryFamily: MandataryFamilyCode | string;
  assignmentMode: MandateAssignmentMode | string;
  institutionalMandataryName: string | null;
  institutionalMandataryNit: string | null;
  chamberCity: string | null;
  mandatarySigla: string | null;
  hasExplicitConfig: boolean;
  rowVersion: number | null;
  customTemplateKind: CustomTemplateKind | string;
  customTemplateFileName: string | null;
  customTemplateBody: string | null;
  hasCustomTemplate: boolean;
  /** Mandatario global del OT (aplica si la empresa no tiene default propio). */
  defaultMandateSignerId: string | null;
  defaultMandateSignerName: string | null;
  defaultMandateSignerDocumentType: string | null;
  defaultMandateSignerDocumentNumber: string | null;
  defaultMandateSignerIntegrityHash: string | null;
  /** Compañías con Persona jurídica explícita. El resto usa Persona natural. */
  explicitPersonaJuridica: number;
  /** Compañías con Mandato abierto explícito. */
  explicitMandatoAbierto: number;
}

export interface UpsertMandateOtConfigBody {
  templateCode: string;
  requiresForNaturalPerson: boolean;
  mandataryFamily: string;
  assignmentMode: string;
  institutionalMandataryName?: string | null;
  institutionalMandataryNit?: string | null;
  chamberCity?: string | null;
  mandatarySigla?: string | null;
  rowVersion?: number | null;
  defaultMandateSignerId?: string | null;
}

function mapView(raw: Record<string, unknown>): MandateOtConfigView {
  return {
    officeId: String(raw.officeId ?? raw.OfficeId ?? ""),
    code: String(raw.code ?? raw.Code ?? ""),
    name: String(raw.name ?? raw.Name ?? ""),
    templateCode: String(raw.templateCode ?? raw.TemplateCode ?? "generico"),
    configuredTemplateCode: String(
      raw.configuredTemplateCode ?? raw.ConfiguredTemplateCode ?? "auto",
    ),
    requiresForNaturalPerson: Boolean(raw.requiresForNaturalPerson ?? raw.RequiresForNaturalPerson),
    mandataryFamily: String(raw.mandataryFamily ?? raw.MandataryFamily ?? "individuo"),
    assignmentMode: String(raw.assignmentMode ?? raw.AssignmentMode ?? "signer"),
    institutionalMandataryName:
      (raw.institutionalMandataryName as string | null | undefined) ??
      (raw.InstitutionalMandataryName as string | null | undefined) ??
      null,
    institutionalMandataryNit:
      (raw.institutionalMandataryNit as string | null | undefined) ??
      (raw.InstitutionalMandataryNit as string | null | undefined) ??
      null,
    chamberCity:
      (raw.chamberCity as string | null | undefined) ??
      (raw.ChamberCity as string | null | undefined) ??
      null,
    mandatarySigla:
      (raw.mandatarySigla as string | null | undefined) ??
      (raw.MandatarySigla as string | null | undefined) ??
      null,
    hasExplicitConfig: Boolean(raw.hasExplicitConfig ?? raw.HasExplicitConfig),
    rowVersion: parseRowVersion(raw.rowVersion ?? raw.RowVersion),
    customTemplateKind: String(raw.customTemplateKind ?? raw.CustomTemplateKind ?? "none"),
    customTemplateFileName:
      (raw.customTemplateFileName as string | null | undefined) ??
      (raw.CustomTemplateFileName as string | null | undefined) ??
      null,
    customTemplateBody:
      (raw.customTemplateBody as string | null | undefined) ??
      (raw.CustomTemplateBody as string | null | undefined) ??
      null,
    hasCustomTemplate: Boolean(raw.hasCustomTemplate ?? raw.HasCustomTemplate),
    defaultMandateSignerId:
      (raw.defaultMandateSignerId as string | null | undefined) ??
      (raw.DefaultMandateSignerId as string | null | undefined) ??
      null,
    defaultMandateSignerName:
      (raw.defaultMandateSignerName as string | null | undefined) ??
      (raw.DefaultMandateSignerName as string | null | undefined) ??
      null,
    defaultMandateSignerDocumentType:
      optionalString(raw.defaultMandateSignerDocumentType ?? raw.DefaultMandateSignerDocumentType),
    defaultMandateSignerDocumentNumber:
      optionalString(raw.defaultMandateSignerDocumentNumber ?? raw.DefaultMandateSignerDocumentNumber),
    defaultMandateSignerIntegrityHash:
      optionalString(raw.defaultMandateSignerIntegrityHash ?? raw.DefaultMandateSignerIntegrityHash),
    explicitPersonaJuridica: Number(raw.explicitPersonaJuridica ?? raw.ExplicitPersonaJuridica ?? 0) || 0,
    explicitMandatoAbierto: Number(raw.explicitMandatoAbierto ?? raw.ExplicitMandatoAbierto ?? 0) || 0,
  };
}

export async function listMandateOtConfigs(signal?: AbortSignal): Promise<MandateOtConfigView[]> {
  const data = await apiFetch<MandateOtConfigView[] | { items: MandateOtConfigView[] }>(base, {
    signal,
  });
  const rows = Array.isArray(data) ? data : (data?.items ?? []);
  return rows.map((r) => mapView(r as unknown as Record<string, unknown>));
}

function isOtHubPath(): boolean {
  if (typeof window === "undefined") return false;
  return window.location.pathname.includes("/admin/transit-offices/");
}

/** Rutas de un OT: hub usa API OtModule; Plataforma sigue en SuperAdmin. */
function mandateOfficeRoot(officeId: string): string {
  const id = encodeURIComponent(officeId);
  if (isOtHubPath()) {
    return `/api/v1/admin/ot/offices/${id}/mandatos`;
  }
  return `${base}/ot/${id}`;
}

export async function fetchMandateOtConfig(
  officeId: string,
  signal?: AbortSignal,
): Promise<MandateOtConfigView> {
  const data = await apiFetch<MandateOtConfigView>(mandateOfficeRoot(officeId), { signal });
  return mapView(data as unknown as Record<string, unknown>);
}

export async function upsertMandateOtConfig(
  officeId: string,
  body: UpsertMandateOtConfigBody,
  signal?: AbortSignal,
): Promise<MandateOtConfigView> {
  const data = await apiFetch<MandateOtConfigView>(mandateOfficeRoot(officeId), {
    method: "PUT",
    body,
    signal,
  });
  return mapView(data as unknown as Record<string, unknown>);
}

export async function deleteMandateOtConfig(officeId: string, signal?: AbortSignal): Promise<void> {
  await apiFetch<void>(`${base}/ot/${officeId}`, { method: "DELETE", signal });
}

export async function uploadMandateOtPdfTemplate(
  officeId: string,
  file: File,
  signal?: AbortSignal,
): Promise<MandateOtConfigView> {
  const baseUrl =
    API_BASE_URL || (typeof window !== "undefined" ? window.location.origin : "http://localhost:3000");
  const url = new URL(`${base}/ot/${officeId}/template`, baseUrl);
  const token = getToken();
  const form = new FormData();
  form.append("file", file);
  const headers: Record<string, string> = {};
  if (token) headers.Authorization = `Bearer ${token}`;

  const response = await fetch(url.toString(), { method: "POST", headers, body: form, signal });
  if (!response.ok) {
    let detail: unknown = null;
    try {
      detail = await response.json();
    } catch {
      /* ignore */
    }
    // Mensaje desde el ProblemDetails del backend, nunca la ruta/status crudos (Bug #11626).
    throw new ApiError(response.status, friendlyErrorMessage(detail as Record<string, unknown> | null), detail);
  }
  const raw = (await response.json()) as Record<string, unknown>;
  return mapView(raw);
}

export async function saveMandateOtEditorBody(
  officeId: string,
  body: string,
  rowVersion?: number | null,
  signal?: AbortSignal,
): Promise<MandateOtConfigView> {
  const data = await apiFetch<MandateOtConfigView>(`${base}/ot/${officeId}/template/editor`, {
    method: "PUT",
    body: { body, rowVersion },
    signal,
  });
  return mapView(data as unknown as Record<string, unknown>);
}

export async function deleteMandateOtCustomTemplate(
  officeId: string,
  signal?: AbortSignal,
): Promise<MandateOtConfigView> {
  const data = await apiFetch<MandateOtConfigView>(`${base}/ot/${officeId}/template`, {
    method: "DELETE",
    signal,
  });
  return mapView(data as unknown as Record<string, unknown>);
}

/**
 * HU #13174 — formato de contrato del catálogo del backend (GET /mandatos/formatos). Es la única fuente
 * de códigos y nombres: el frontend no mantiene una lista propia.
 */
export interface MandatoFormatView {
  code: string;
  /** Nombre vigente (el que edita el Super Admin). */
  name: string;
  /** Tipo de mandato por defecto de la redacción. */
  assignmentMode: MandateAssignmentMode | string;
  /** Redacción base que emite el generador; null en la automática, que solo delega. */
  baseRedaction: string | null;
  selectableAsRedaction: boolean;
  delegatesToOfficeTemplate: boolean;
  /** HU #13175 — número de la versión vigente de la plantilla (0 = la de fábrica). */
  currentVersion: number;
  hasCustomTemplate: boolean;
  /** Control de concurrencia del PUT; null mientras el formato no tenga fila propia. */
  rowVersion: number | null;
  /** Última edición (ISO) o null si nunca se editó. */
  updatedAt: string | null;
}

function mapFormat(raw: Record<string, unknown>): MandatoFormatView {
  const baseRedaction = typeof raw.baseRedaction === "string" ? raw.baseRedaction : null;
  return {
    code: String(raw.code ?? ""),
    name: String(raw.name ?? raw.code ?? ""),
    assignmentMode: String(raw.assignmentMode ?? "signer"),
    baseRedaction,
    selectableAsRedaction: raw.selectableAsRedaction === true || baseRedaction !== null,
    delegatesToOfficeTemplate: raw.delegatesToOfficeTemplate === true,
    currentVersion: typeof raw.currentVersion === "number" ? raw.currentVersion : 0,
    hasCustomTemplate: raw.hasCustomTemplate === true,
    rowVersion: parseRowVersion(raw.rowVersion),
    updatedAt: typeof raw.updatedAt === "string" ? raw.updatedAt : null,
  };
}

/** Versión publicada de la plantilla de un formato (sin el texto). */
export interface MandatoFormatVersionInfo {
  versionNumber: number;
  sha256: string;
  createdAt: string;
  createdBy: string | null;
}

export interface MandatoFormatDetail {
  format: MandatoFormatView;
  /** Texto de la versión vigente, o null si el formato usa la redacción de fábrica. */
  body: string | null;
  versions: MandatoFormatVersionInfo[];
}

function mapVersion(raw: Record<string, unknown>): MandatoFormatVersionInfo {
  return {
    versionNumber: Number(raw.versionNumber ?? 0),
    sha256: String(raw.sha256 ?? ""),
    createdAt: String(raw.createdAt ?? ""),
    createdBy: typeof raw.createdBy === "string" ? raw.createdBy : null,
  };
}

export async function getMandatoFormat(code: string, signal?: AbortSignal): Promise<MandatoFormatDetail> {
  const data = await apiFetch<Record<string, unknown>>(`${base}/formatos/${encodeURIComponent(code)}`, { signal });
  const versions = Array.isArray(data.versions) ? (data.versions as Record<string, unknown>[]) : [];
  return {
    format: mapFormat((data.format ?? {}) as Record<string, unknown>),
    body: typeof data.body === "string" ? data.body : null,
    versions: versions.map(mapVersion),
  };
}

export async function getMandatoFormatVersion(
  code: string,
  versionNumber: number,
  signal?: AbortSignal,
): Promise<MandatoFormatVersionInfo & { body: string }> {
  const data = await apiFetch<Record<string, unknown>>(
    `${base}/formatos/${encodeURIComponent(code)}/versions/${versionNumber}`,
    { signal },
  );
  return { ...mapVersion(data), body: String(data.body ?? "") };
}

/** Campo ausente = no tocar; `body` publica una versión nueva de la plantilla. */
export interface UpdateMandatoFormatBody {
  rowVersion: number | null;
  name?: string;
  assignmentMode?: string;
  body?: string;
}

export interface UpdateMandatoFormatResult {
  format: MandatoFormatView;
  changed: boolean;
  publishedVersion: number | null;
}

export async function updateMandatoFormat(
  code: string,
  body: UpdateMandatoFormatBody,
  signal?: AbortSignal,
): Promise<UpdateMandatoFormatResult> {
  const data = await apiFetch<Record<string, unknown>>(`${base}/formatos/${encodeURIComponent(code)}`, {
    method: "PUT",
    body,
    signal,
  });
  return {
    format: mapFormat((data.format ?? {}) as Record<string, unknown>),
    changed: data.changed === true,
    publishedVersion: typeof data.publishedVersion === "number" ? data.publishedVersion : null,
  };
}

/** Vista previa (PDF de muestra) de la plantilla en borrador; no publica ninguna versión. */
export async function previewMandatoFormatDraft(
  code: string,
  body: string,
  signal?: AbortSignal,
): Promise<Blob> {
  return postPdf(`${base}/formatos/${encodeURIComponent(code)}/preview`, { body }, signal);
}

/** Variable desconocida que devuelve el API al guardar o previsualizar (400 plantilla_variable_invalida). */
export interface MandatoUnknownVariable {
  name: string;
  line?: number;
  column?: number;
}

/** Extrae `{ error, unknownVariables }` del cuerpo de un ApiError del editor de formatos. */
export function readFormatError(body: unknown): { error: string | null; unknownVariables: MandatoUnknownVariable[] } {
  const raw = (body ?? {}) as Record<string, unknown>;
  const vars = Array.isArray(raw.unknownVariables) ? (raw.unknownVariables as Record<string, unknown>[]) : [];
  return {
    error: typeof raw.error === "string" ? raw.error : null,
    unknownVariables: vars.map((v) => ({
      name: String(v.name ?? ""),
      line: typeof v.line === "number" ? v.line : undefined,
      column: typeof v.column === "number" ? v.column : undefined,
    })),
  };
}

export async function listMandatoFormats(signal?: AbortSignal): Promise<MandatoFormatView[]> {
  const data = await apiFetch<{ items?: Record<string, unknown>[] }>(`${base}/formatos`, { signal });
  const items = Array.isArray(data?.items) ? data.items : [];
  return items.map(mapFormat);
}

/** HU #13174 — el hub OT lee el mismo catálogo (nombres, sin el cuerpo de la plantilla). */
export async function listOtMandatoFormats(
  officeId: string,
  signal?: AbortSignal,
): Promise<MandatoFormatView[]> {
  const data = await apiFetch<{ items?: Record<string, unknown>[] }>(
    `/api/v1/admin/ot/offices/${encodeURIComponent(officeId)}/mandatos/formatos`,
    { signal },
  );
  const items = Array.isArray(data?.items) ? data.items : [];
  return items.map(mapFormat);
}

export async function fetchMandatoTemplatePreview(
  templateCode: string,
  signal?: AbortSignal,
  officeId?: string,
): Promise<Blob> {
  if (officeId && isOtHubPath()) {
    return fetchPdf(
      `${mandateOfficeRoot(officeId)}/templates/${encodeURIComponent(templateCode)}/preview`,
      signal,
    );
  }
  return fetchPdf(`${base}/${encodeURIComponent(templateCode)}/preview`, signal);
}

export async function fetchMandateOtPreview(officeId: string, signal?: AbortSignal): Promise<Blob> {
  return fetchPdf(`${mandateOfficeRoot(officeId)}/preview`, signal);
}

export interface CompanyOtMandateRuleView {
  companyTenantId: string;
  companyName: string;
  assignmentMode: MandateAssignmentMode | string;
  mandataryFamily: MandataryFamilyCode | string;
  institutionalMandataryName: string | null;
  institutionalMandataryNit: string | null;
  chamberCity: string | null;
  mandatarySigla: string | null;
  hasExplicitRule: boolean;
  /** Mandatario persona preferido (solo Persona natural). */
  defaultMandateSignerId: string | null;
  companyTaxId: string | null;
  companyCode: string | null;
  defaultMandateSignerName: string | null;
  defaultMandateSignerDocumentType: string | null;
  defaultMandateSignerDocumentNumber: string | null;
  defaultMandateSignerIntegrityHash: string | null;
  /** Token de concurrencia de la regla (HU #13148); null si la compañía hereda el default. */
  rowVersion: number | null;
}

export interface UpsertCompanyOtMandateRuleBody {
  assignmentMode: string;
  mandataryFamily?: string;
  institutionalMandataryName?: string | null;
  institutionalMandataryNit?: string | null;
  chamberCity?: string | null;
  mandatarySigla?: string | null;
  defaultMandateSignerId?: string | null;
  /** Obligatorio cuando la regla ya existe: el API responde 409 row_version_conflict si quedó vieja. */
  rowVersion?: number | null;
}

export function mapCompanyRule(raw: Record<string, unknown>): CompanyOtMandateRuleView {
  const defaultId =
    (raw.defaultMandateSignerId as string | null | undefined) ??
    (raw.DefaultMandateSignerId as string | null | undefined) ??
    null;
  return {
    companyTenantId: String(raw.companyTenantId ?? raw.CompanyTenantId ?? ""),
    companyName: String(raw.companyName ?? raw.CompanyName ?? ""),
    assignmentMode: String(raw.assignmentMode ?? raw.AssignmentMode ?? "signer"),
    mandataryFamily: String(raw.mandataryFamily ?? raw.MandataryFamily ?? "individuo"),
    institutionalMandataryName:
      (raw.institutionalMandataryName as string | null | undefined) ??
      (raw.InstitutionalMandataryName as string | null | undefined) ??
      null,
    institutionalMandataryNit:
      (raw.institutionalMandataryNit as string | null | undefined) ??
      (raw.InstitutionalMandataryNit as string | null | undefined) ??
      null,
    chamberCity:
      (raw.chamberCity as string | null | undefined) ??
      (raw.ChamberCity as string | null | undefined) ??
      null,
    mandatarySigla:
      (raw.mandatarySigla as string | null | undefined) ??
      (raw.MandatarySigla as string | null | undefined) ??
      null,
    hasExplicitRule: Boolean(raw.hasExplicitRule ?? raw.HasExplicitRule),
    defaultMandateSignerId: defaultId ? String(defaultId) : null,
    companyTaxId: optionalString(raw.companyTaxId ?? raw.CompanyTaxId),
    companyCode: optionalString(raw.companyCode ?? raw.CompanyCode),
    defaultMandateSignerName: optionalString(
      raw.defaultMandateSignerName ?? raw.DefaultMandateSignerName,
    ),
    defaultMandateSignerDocumentType: optionalString(
      raw.defaultMandateSignerDocumentType ?? raw.DefaultMandateSignerDocumentType,
    ),
    defaultMandateSignerDocumentNumber: optionalString(
      raw.defaultMandateSignerDocumentNumber ?? raw.DefaultMandateSignerDocumentNumber,
    ),
    defaultMandateSignerIntegrityHash: optionalString(
      raw.defaultMandateSignerIntegrityHash ?? raw.DefaultMandateSignerIntegrityHash,
    ),
    rowVersion: parseRowVersion(raw.rowVersion ?? raw.RowVersion),
  };
}

function optionalString(value: unknown): string | null {
  if (value == null) return null;
  const text = String(value).trim();
  return text.length > 0 ? text : null;
}

export async function listCompanyOtMandateRules(
  officeId: string,
  signal?: AbortSignal,
): Promise<CompanyOtMandateRuleView[]> {
  const data = await apiFetch<
    CompanyOtMandateRuleView[] | { items: CompanyOtMandateRuleView[] }
  >(`${mandateOfficeRoot(officeId)}/company-rules`, { signal });
  const rows = Array.isArray(data) ? data : (data?.items ?? []);
  return rows.map((r) => mapCompanyRule(r as unknown as Record<string, unknown>));
}

export async function upsertCompanyOtMandateRule(
  officeId: string,
  companyTenantId: string,
  body: UpsertCompanyOtMandateRuleBody,
  signal?: AbortSignal,
): Promise<CompanyOtMandateRuleView> {
  const data = await apiFetch<CompanyOtMandateRuleView>(
    `${mandateOfficeRoot(officeId)}/company-rules/${companyTenantId}`,
    { method: "PUT", body, signal },
  );
  return mapCompanyRule(data as unknown as Record<string, unknown>);
}

export async function setOtDefaultSigner(
  officeId: string,
  body: {
    defaultMandateSignerId?: string | null;
    rowVersion?: number | null;
  },
  signal?: AbortSignal,
): Promise<MandateOtConfigView> {
  const data = await apiFetch<MandateOtConfigView>(`${mandateOfficeRoot(officeId)}/default-signer`, {
    method: "PATCH",
    body,
    signal,
  });
  return mapView(data as unknown as Record<string, unknown>);
}

export async function setCompanyDefaultSigner(
  officeId: string,
  companyTenantId: string,
  defaultMandateSignerId: string | null,
  signal?: AbortSignal,
  rowVersion?: number | null,
): Promise<CompanyOtMandateRuleView> {
  const data = await apiFetch<CompanyOtMandateRuleView>(
    `${mandateOfficeRoot(officeId)}/company-rules/${companyTenantId}/default-signer`,
    { method: "PATCH", body: { defaultMandateSignerId, rowVersion }, signal },
  );
  return mapCompanyRule(data as unknown as Record<string, unknown>);
}

export async function deleteCompanyOtMandateRule(
  officeId: string,
  companyTenantId: string,
  signal?: AbortSignal,
  rowVersion?: number | null,
): Promise<void> {
  const query = rowVersion != null ? `?rowVersion=${encodeURIComponent(String(rowVersion))}` : "";
  await apiFetch<void>(`${mandateOfficeRoot(officeId)}/company-rules/${companyTenantId}${query}`, {
    method: "DELETE",
    signal,
  });
}

/** Escenario del simulador (HU #11707). */
export interface MandateSimulationBody {
  officeId: string;
  /** `natural` | `juridica` — tipo de persona del MANDANTE. */
  personType: "natural" | "juridica";
  /** Nulo ⇒ el modo configurado para el organismo. */
  assignmentMode?: MandateAssignmentMode | string | null;
  /** Nulo ⇒ el mandatario sale como dato de muestra. */
  mandateSignerId?: string | null;
  /** Código de `tramites.procedure_types` (`MATRICULA_NUEVA`, `TRASPASO_STANDARD`, …). */
  procedureTypeCode?: string | null;
  /** Alias wizard; el backend lo acepta si no llega `procedureTypeCode`. */
  tipologia?: string | null;
  /** Mismo vocabulario que el simulador FUR. */
  prenda?: "ninguna" | "inscripcion" | "levantamiento" | "ambas" | null;
  cambioColor?: boolean;
  cambioCombustible?: boolean;
  cambioCarroceria?: boolean;
  blindaje?: boolean;
}

export interface MandateSimulatorSignerOption {
  id: string;
  fullName: string;
  documentNumber: string;
  identityVigente: boolean;
  tieneFirmaEnBaul: boolean;
}

export async function listMandateSimulatorSigners(
  officeId: string,
  signal?: AbortSignal,
): Promise<MandateSimulatorSignerOption[]> {
  const data = await apiFetch<{ items: MandateSimulatorSignerOption[] }>(
    `${base}/simulador/ot/${officeId}/mandatarios`,
    { signal },
  );
  return data?.items ?? [];
}

/** PDF de la simulación. El escenario viaja en el cuerpo, así que va por POST. */
export async function fetchMandateSimulationPreview(
  body: MandateSimulationBody,
  signal?: AbortSignal,
): Promise<Blob> {
  return postPdf(`${base}/simulador/preview`, body, signal);
}

/**
 * Envío por correo de la simulación. **Sin consumidores**: la función quedó oculta en la interfaz
 * (el simulador solo previsualiza). Se conserva para no perder el trabajo si vuelve a pedirse.
 */
export async function sendMandateSimulation(
  body: MandateSimulationBody & { toEmail: string; toName?: string | null },
  signal?: AbortSignal,
): Promise<string> {
  const data = await apiFetch<{ message?: string }>(`${base}/simulador/enviar`, {
    method: "POST",
    body,
    signal,
  });
  return data?.message ?? "Simulación enviada.";
}

async function postPdf(path: string, body: unknown, signal?: AbortSignal): Promise<Blob> {
  const baseUrl =
    API_BASE_URL || (typeof window !== "undefined" ? window.location.origin : "http://localhost:3000");
  const url = new URL(path, baseUrl);
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

async function fetchPdf(path: string, signal?: AbortSignal): Promise<Blob> {
  const baseUrl =
    API_BASE_URL || (typeof window !== "undefined" ? window.location.origin : "http://localhost:3000");
  const url = new URL(path, baseUrl);
  const token = getToken();
  const headers: Record<string, string> = {};
  if (token) headers.Authorization = `Bearer ${token}`;

  const response = await fetch(url.toString(), { method: "GET", headers, signal });
  if (!response.ok) {
    let detail: unknown = null;
    try {
      const text = await response.text();
      detail = text ? JSON.parse(text) : null;
    } catch {
      /* ignore */
    }
    // Mensaje desde el ProblemDetails del backend, nunca la ruta/status crudos (Bug #11626).
    throw new ApiError(response.status, friendlyErrorMessage(detail as Record<string, unknown> | null), detail);
  }

  const blob = await response.blob();
  return blob.type === "application/pdf" ? blob : new Blob([blob], { type: "application/pdf" });
}

function parseRowVersion(value: unknown): number | null {
  if (value === null || value === undefined || value === "") return null;
  const n = typeof value === "number" ? value : Number(value);
  return Number.isFinite(n) ? n : null;
}
