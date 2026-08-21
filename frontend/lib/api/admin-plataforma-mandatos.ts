// Cliente SuperAdmin — Plataforma → Mandatos (config por OT + plantilla propia + preview + extract).
import { API_BASE_URL, apiFetch, friendlyErrorMessage, getToken } from "./client";
import { ApiError } from "./types";
import type {
  MandateAssignmentMode,
  MandatoConfiguredTemplateCode,
  MandatoTemplateCode,
} from "@/lib/plataforma/mandato-templates";

const base = "/api/v1/admin/plataforma/mandatos";

export type MandataryFamilyCode = "individuo" | "organismo_transito";
export type CustomTemplateKind = "none" | "pdf" | "editor";

export interface MandateOtConfigView {
  officeId: string;
  code: string;
  name: string;
  /** Redacción EFECTIVA que se emite hoy para este OT (con `auto` ya resuelto). */
  templateCode: MandatoTemplateCode | string;
  /**
   * Redacción ELEGIDA, tal cual está guardada (`auto` si el OT no fija ninguna). Es la que
   * preselecciona el selector: preseleccionar con la efectiva convertiría un "automática" en una
   * redacción fija al guardar sin tocar nada.
   */
  configuredTemplateCode: MandatoConfiguredTemplateCode | string;
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
  /** Mandatario persona preferido (solo Persona/RL). */
  defaultMandateSignerId: string | null;
}

export interface UpsertCompanyOtMandateRuleBody {
  assignmentMode: string;
  mandataryFamily?: string;
  institutionalMandataryName?: string | null;
  institutionalMandataryNit?: string | null;
  chamberCity?: string | null;
  mandatarySigla?: string | null;
  defaultMandateSignerId?: string | null;
}

function mapCompanyRule(raw: Record<string, unknown>): CompanyOtMandateRuleView {
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
    // Mensaje desde el ProblemDetails del backend, nunca la ruta/status crudos (Bug #11626).
    throw new ApiError(response.status, friendlyErrorMessage(detail as Record<string, unknown> | null), detail);
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

/** Escenario del simulador (HU #11707). */
export interface MandateSimulationBody {
  officeId: string;
  /** `natural` | `juridica` — tipo de persona del MANDANTE. */
  personType: "natural" | "juridica";
  /** Nulo ⇒ el modo configurado para el organismo. */
  assignmentMode?: MandateAssignmentMode | string | null;
  /** Nulo ⇒ el mandatario sale como dato de muestra. */
  mandateSignerId?: string | null;
  /** `matricula_inicial` | `traspaso_standard` — cambia el objeto del contrato. */
  tipologia?: string | null;
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
