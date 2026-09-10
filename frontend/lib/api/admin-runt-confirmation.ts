// Cliente de Administración → Plataforma → Confirmación RUNT (Epic #12234, Feature #12276).
// Contratos en `services/core-api/src/Flit.Api/Endpoints/AdminRuntConfirmationEndpoints.cs`
// (fuente de verdad). Configuración: HU #12277; historial, corridas y consulta manual: HU #12310.
import { apiFetch } from "./client";
import { ApiError } from "./types";

const base = "/api/v1/admin/runt-confirmation";

export type RuntConfirmationProviderKey = "kyverum_runt" | "verifik";

export const RUNT_CONFIRMATION_PROVIDERS: ReadonlyArray<{ key: RuntConfirmationProviderKey; label: string }> = [
  { key: "kyverum_runt", label: "Kyverum" },
  { key: "verifik", label: "Verifik" },
];

export interface RuntConfirmationSettings {
  enabled: boolean;
  /** `HH:mm`, hora local de Bogotá. */
  runAtLocal: string;
  providerKey: RuntConfirmationProviderKey;
  graceDays: number;
  discrepancyAfterRuns: number;
  maxAttempts: number;
  updatedAt: string | null;
  updatedBy: string | null;
}

/** Los seis valores que viajan en el PUT: la configuración se guarda completa, no por parches. */
export type RuntConfirmationSettingsInput = Pick<
  RuntConfirmationSettings,
  "enabled" | "runAtLocal" | "providerKey" | "graceDays" | "discrepancyAfterRuns" | "maxAttempts"
>;

/** Campo inválido según el backend (400 `configuracion_invalida`). Mismo nombre camelCase del contrato. */
export interface RuntConfirmationFieldError {
  field: keyof RuntConfirmationSettingsInput | string;
  message: string;
}

export async function getRuntConfirmationSettings(signal?: AbortSignal): Promise<RuntConfirmationSettings> {
  return apiFetch<RuntConfirmationSettings>(`${base}/settings`, { signal });
}

/**
 * `PUT /settings`. Un 400 con `errors[]` se traduce a {@link RuntConfirmationSettingsInvalidError}
 * para que la pantalla marque el campo; cualquier otro fallo sube tal cual.
 */
export async function putRuntConfirmationSettings(
  input: RuntConfirmationSettingsInput,
): Promise<RuntConfirmationSettings> {
  try {
    return await apiFetch<RuntConfirmationSettings>(`${base}/settings`, { method: "PUT", body: input });
  } catch (err) {
    const errors = fieldErrorsOf(err);
    if (errors) throw new RuntConfirmationSettingsInvalidError(errors);
    throw err;
  }
}

export class RuntConfirmationSettingsInvalidError extends Error {
  constructor(public readonly errors: RuntConfirmationFieldError[]) {
    super("La configuración tiene valores fuera de rango.");
    this.name = "RuntConfirmationSettingsInvalidError";
  }
}

function fieldErrorsOf(err: unknown): RuntConfirmationFieldError[] | null {
  if (!(err instanceof ApiError) || err.status !== 400) return null;
  const body = err.body as { error?: string; errors?: RuntConfirmationFieldError[] } | undefined;
  if (body?.error !== "configuracion_invalida" || !Array.isArray(body.errors)) return null;
  return body.errors.filter((e) => typeof e?.field === "string" && typeof e?.message === "string");
}

// ── Historial (HU #12310) ───────────────────────────────────────────────────

export type RuntConfirmationVerdict = "confirmed" | "pending" | "discrepancy" | "unverifiable" | "error";

export const RUNT_VERDICT_LABEL: Record<RuntConfirmationVerdict, string> = {
  confirmed: "Confirmado",
  pending: "Pendiente",
  discrepancy: "Discrepancia",
  unverifiable: "No verificable",
  error: "Error de proveedor",
};

export type RuntConfirmationQueryKind = "vin" | "plate" | "plate_pair" | "vin_plate" | "reevaluation";

export interface RuntConfirmationAttemptRow {
  id: string;
  queriedAt: string;
  procedureInstanceId: string;
  referenceNumber: string;
  procedureTypeCode: string;
  procedureTypeName: string;
  family: string;
  plate: string | null;
  vin: string | null;
  tenantId: string;
  tenantName: string | null;
  providerKey: RuntConfirmationProviderKey | string;
  queryKind: RuntConfirmationQueryKind | string;
  attemptNo: number;
  verdict: RuntConfirmationVerdict;
  reasonText: string;
  ruleVersion: string;
  runId: string | null;
  requestedBy: string | null;
  flagApplied: string | null;
  hasRaw: boolean;
}

export interface RuntConfirmationAttemptDetail {
  row: RuntConfirmationAttemptRow;
  rawPayloadId: string | null;
  sellerRawPayloadId: string | null;
  reevaluatedFromAttemptId: string | null;
  procedureStatus: string | null;
  runtConfirmedAt: string | null;
  runtAttempts: number;
  runtFlag: string | null;
}

export interface RuntConfirmationRun {
  id: string;
  trigger: "scheduled" | "manual";
  startedAt: string;
  finishedAt: string | null;
  providerKey: string | null;
  skippedReason: "disabled" | "already_running" | null;
  consulted: number;
  confirmed: number;
  pending: number;
  discrepancies: number;
  unverifiable: number;
  errors: number;
  providerCalls: number;
  errorMessage: string | null;
}

export interface Page<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface RuntConfirmationAttemptsFilter {
  runId?: string;
  verdict?: RuntConfirmationVerdict | "";
  procedureTypeCode?: string;
  procedureInstanceId?: string;
  search?: string;
  from?: string;
  to?: string;
}

/** Tope por página del backend (`RuntConfirmationAttemptsQuery.MaxPageSize`); el export recorre con él. */
export const RUNT_ATTEMPTS_MAX_PAGE_SIZE = 500;

export async function listRuntConfirmationAttempts(
  filter: RuntConfirmationAttemptsFilter,
  page: number,
  pageSize: number,
  signal?: AbortSignal,
): Promise<Page<RuntConfirmationAttemptRow>> {
  return apiFetch<Page<RuntConfirmationAttemptRow>>(`${base}/attempts`, {
    signal,
    query: {
      runId: filter.runId || undefined,
      verdict: filter.verdict || undefined,
      procedureTypeCode: filter.procedureTypeCode || undefined,
      procedureInstanceId: filter.procedureInstanceId || undefined,
      search: filter.search?.trim() || undefined,
      from: filter.from || undefined,
      to: filter.to || undefined,
      page,
      pageSize,
    },
  });
}

export async function getRuntConfirmationAttempt(id: string, signal?: AbortSignal): Promise<RuntConfirmationAttemptDetail> {
  return apiFetch<RuntConfirmationAttemptDetail>(`${base}/attempts/${encodeURIComponent(id)}`, { signal });
}

/** JSON crudo tal como se guardó: `{ primary, seller? }` (la segunda: el vendedor en traspaso, el desempate por placa en matrícula). */
export async function getRuntConfirmationAttemptRaw(
  id: string,
  signal?: AbortSignal,
): Promise<{ primary: unknown; seller?: unknown }> {
  return apiFetch<{ primary: unknown; seller?: unknown }>(`${base}/attempts/${encodeURIComponent(id)}/raw`, { signal });
}

export async function listRuntConfirmationRuns(page: number, pageSize: number, signal?: AbortSignal): Promise<Page<RuntConfirmationRun>> {
  return apiFetch<Page<RuntConfirmationRun>>(`${base}/runs`, { signal, query: { page, pageSize } });
}

/** `null` cuando nunca ha corrido (204). */
export async function getLatestRuntConfirmationRun(signal?: AbortSignal): Promise<RuntConfirmationRun | null> {
  const run = await apiFetch<RuntConfirmationRun | undefined>(`${base}/runs/latest`, { signal });
  return run ?? null;
}

export type ConsultNowConflict = "tramite_no_aprobado" | "tipo_fuera_de_alcance" | "ya_confirmado";

export const CONSULT_NOW_CONFLICT_LABEL: Record<ConsultNowConflict, string> = {
  tramite_no_aprobado: "El trámite no está aprobado: solo se confirman trámites aprobados.",
  tipo_fuera_de_alcance: "Este tipo de trámite está fuera del alcance de la confirmación.",
  ya_confirmado: "El trámite ya está confirmado en el RUNT.",
};

export class ConsultNowConflictError extends Error {
  constructor(public readonly code: ConsultNowConflict | string) {
    super(CONSULT_NOW_CONFLICT_LABEL[code as ConsultNowConflict] ?? "No se puede consultar este trámite ahora.");
    this.name = "ConsultNowConflictError";
  }
}

/** `POST /procedures/{id}/consult-now`. Un 409 se traduce a {@link ConsultNowConflictError} con el motivo. */
export async function consultRuntNow(
  procedureInstanceId: string,
): Promise<{ attempt: RuntConfirmationAttemptRowLike | null; run: RuntConfirmationRun }> {
  try {
    return await apiFetch(`${base}/procedures/${encodeURIComponent(procedureInstanceId)}/consult-now`, { method: "POST" });
  } catch (err) {
    if (err instanceof ApiError && err.status === 409) {
      const body = err.body as { error?: string } | undefined;
      throw new ConsultNowConflictError(body?.error ?? "conflicto");
    }
    throw err;
  }
}

/** El intento que devuelve la consulta manual es la entidad cruda (sin el join del listado). */
export interface RuntConfirmationAttemptRowLike {
  id: string;
  procedureInstanceId: string;
  attemptNo: number;
  queriedAt: string;
  providerKey: string;
  queryKind: string;
  verdict: RuntConfirmationVerdict;
  reasonText: string;
  ruleVersion: string;
  runId: string | null;
  requestedBy: string | null;
  flagApplied: string | null;
  rawPayloadId: string | null;
  sellerRawPayloadId: string | null;
}
