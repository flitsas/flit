// Cliente SuperAdmin — Plataforma → Mandatos (config por OT + plantilla propia + preview + extract).
import { API_BASE_URL, apiFetch, getToken } from "./client";
import { ApiError } from "./types";
import type {
  MandateAssignmentMode,
  MandatoTemplateCode,
} from "@/lib/plataforma/mandato-templates";

const base = "/api/v1/admin/plataforma/mandatos";

export type MandataryFamilyCode = "individuo" | "organismo_transito";
export type CustomTemplateKind = "none" | "pdf" | "editor";

export interface MandateOtConfigView {
  officeId: string;
  code: string;
  name: string;
  templateCode: MandatoTemplateCode | string;
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
}

export interface MandateConfigExtractResult {
  suggestedTemplateCode: string;
  requiresForNaturalPerson: boolean;
  mandataryFamily: string;
  assignmentMode: string;
  institutionalMandataryName: string | null;
  institutionalMandataryNit: string | null;
  chamberCity: string | null;
  mandatarySigla: string | null;
  notes: string | null;
}

function mapView(raw: Record<string, unknown>): MandateOtConfigView {
  return {
    officeId: String(raw.officeId ?? raw.OfficeId ?? ""),
    code: String(raw.code ?? raw.Code ?? ""),
    name: String(raw.name ?? raw.Name ?? ""),
    templateCode: String(raw.templateCode ?? raw.TemplateCode ?? "generico"),
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
    rowVersion:
      (raw.rowVersion as number | null | undefined) ??
      (raw.RowVersion as number | null | undefined) ??
      null,
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
  };
}

export async function listMandateOtConfigs(signal?: AbortSignal): Promise<MandateOtConfigView[]> {
  const data = await apiFetch<MandateOtConfigView[] | { items: MandateOtConfigView[] }>(base, {
    signal,
  });
  const rows = Array.isArray(data) ? data : (data?.items ?? []);
  return rows.map((r) => mapView(r as unknown as Record<string, unknown>));
}

export async function upsertMandateOtConfig(
  officeId: string,
  body: UpsertMandateOtConfigBody,
  signal?: AbortSignal,
): Promise<MandateOtConfigView> {
  const data = await apiFetch<MandateOtConfigView>(`${base}/ot/${officeId}`, {
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
    throw new ApiError(response.status, `Error ${response.status} al subir plantilla`, detail);
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

export async function fetchMandatoTemplatePreview(
  templateCode: MandatoTemplateCode | string,
  signal?: AbortSignal,
): Promise<Blob> {
  return fetchPdf(`${base}/${encodeURIComponent(templateCode)}/preview`, signal);
}

export async function fetchMandateOtPreview(officeId: string, signal?: AbortSignal): Promise<Blob> {
  return fetchPdf(`${base}/ot/${encodeURIComponent(officeId)}/preview`, signal);
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
}

export interface UpsertCompanyOtMandateRuleBody {
  assignmentMode: string;
  mandataryFamily?: string;
  institutionalMandataryName?: string | null;
  institutionalMandataryNit?: string | null;
  chamberCity?: string | null;
  mandatarySigla?: string | null;
}

function mapCompanyRule(raw: Record<string, unknown>): CompanyOtMandateRuleView {
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
  };
}

export async function listCompanyOtMandateRules(
  officeId: string,
  signal?: AbortSignal,
): Promise<CompanyOtMandateRuleView[]> {
  const data = await apiFetch<
    CompanyOtMandateRuleView[] | { items: CompanyOtMandateRuleView[] }
  >(`${base}/ot/${officeId}/company-rules`, { signal });
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
    `${base}/ot/${officeId}/company-rules/${companyTenantId}`,
    { method: "PUT", body, signal },
  );
  return mapCompanyRule(data as unknown as Record<string, unknown>);
}

export async function deleteCompanyOtMandateRule(
  officeId: string,
  companyTenantId: string,
  signal?: AbortSignal,
): Promise<void> {
  await apiFetch<void>(`${base}/ot/${officeId}/company-rules/${companyTenantId}`, {
    method: "DELETE",
    signal,
  });
}

export async function extractMandateConfigFromFile(
  file: File,
  signal?: AbortSignal,
): Promise<MandateConfigExtractResult> {
  const baseUrl =
    API_BASE_URL || (typeof window !== "undefined" ? window.location.origin : "http://localhost:3000");
  const url = new URL(`${base}/extract`, baseUrl);
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
    throw new ApiError(response.status, `Error ${response.status} al extraer mandato`, detail);
  }

  const raw = (await response.json()) as Record<string, unknown>;
  return {
    suggestedTemplateCode: String(raw.suggestedTemplateCode ?? raw.SuggestedTemplateCode ?? "generico"),
    requiresForNaturalPerson: Boolean(
      raw.requiresForNaturalPerson ?? raw.RequiresForNaturalPerson,
    ),
    mandataryFamily: String(raw.mandataryFamily ?? raw.MandataryFamily ?? "individuo"),
    assignmentMode: String(raw.assignmentMode ?? raw.AssignmentMode ?? "signer"),
    institutionalMandataryName:
      (raw.institutionalMandataryName as string | null) ??
      (raw.InstitutionalMandataryName as string | null) ??
      null,
    institutionalMandataryNit:
      (raw.institutionalMandataryNit as string | null) ??
      (raw.InstitutionalMandataryNit as string | null) ??
      null,
    chamberCity:
      (raw.chamberCity as string | null) ?? (raw.ChamberCity as string | null) ?? null,
    mandatarySigla:
      (raw.mandatarySigla as string | null) ?? (raw.MandatarySigla as string | null) ?? null,
    notes: (raw.notes as string | null) ?? (raw.Notes as string | null) ?? null,
  };
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
    throw new ApiError(response.status, `Error ${response.status} al previsualizar mandato`, detail);
  }

  const blob = await response.blob();
  return blob.type === "application/pdf" ? blob : new Blob([blob], { type: "application/pdf" });
}
