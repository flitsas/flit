/**
 * Cliente de la invitación masiva de usuarios por Excel (Feature #14420 · ADR-0077).
 *
 * El backend es una operación síncrona acotada en DOS fases sin estado: `dryRun=true` valida y
 * previsualiza sin crear nada; `dryRun=false` revalida todo y crea las invitaciones. El lote y sus
 * filas no se persisten, así que el reporte vive solo en la respuesta.
 *
 * Dos familias de rutas, mismo contrato:
 * - Security (perfil Gestor): `GET /security/invitations/bulk/template` y
 *   `POST /security/invitations/bulk?dryRun=`. El SuperAdmin acota con `targetTenantId`; el
 *   AdminCompany no manda nada (el JWT fija su compañía).
 * - OT: `GET /admin/ot/users/invite/bulk/template` y `POST /admin/ot/users/invite/bulk?dryRun=`. El
 *   SuperAdmin acota con `transitOfficeId`; ot_admin / Gestor OT lo toman del JWT.
 *
 * Va por `fetch` directo para el POST (multipart: el navegador pone el boundary) y por
 * `downloadFile` para la plantilla, ambos con el JWT y el mismo mapeo de errores que `apiFetch`.
 * El mensaje de un error nunca incluye contenido del archivo: correos y nombres son PII.
 *
 * Uso de ejemplo:
 *   const client = createSecurityBulkInvitationsClient({ targetTenantId });
 *   const preview = await client.submit(file, { dryRun: true });
 *   const result = await client.submit(file, { dryRun: false });
 */
import { API_BASE_URL, friendlyErrorMessage, getToken } from "./client";
import { sessionAwareBase } from "@/lib/api/base-url";
import { downloadFile } from "./download";
import { ApiError } from "./types";

export const SECURITY_BULK_INVITATIONS_PATH = "/api/v1/security/invitations/bulk";
export const OT_BULK_INVITATIONS_PATH = "/api/v1/admin/ot/users/invite/bulk";

/** Tope de filas por archivo (`BulkInvitations:MaxRows`, ADR-0077). El backend vuelve a validarlo. */
export const BULK_INVITE_MAX_ROWS = 50;
/** Tope de tamaño del archivo (`BulkInvitations:MaxFileBytes` = 1 MB, ADR-0077). */
export const BULK_INVITE_MAX_FILE_BYTES = 1024 * 1024;

/**
 * El nginx corta a los 60 s y el commit tiene un deadline propio de 50 s: pasado este tiempo la
 * respuesta ya no va a llegar, pero las invitaciones procesadas SÍ pudieron crearse.
 */
export const BULK_INVITE_REQUEST_TIMEOUT_MS = 65_000;

export type BulkInvitationRowStatus = "valid" | "created" | "rejected" | "not_processed";

export interface BulkInvitationRowResult {
  /** Fila del Excel (1 = encabezado), tal como la ve el usuario al corregir. */
  rowNumber: number;
  email: string | null;
  fullName: string | null;
  roleName: string | null;
  status: BulkInvitationRowStatus;
  /** Código estable del rechazo (`EMAIL_ALREADY_IN_USE`, …). `null` si la fila va bien. */
  errorCode: string | null;
}

export interface BulkInvitationReport {
  dryRun: boolean;
  total: number;
  /** Filas que pasaron la validación (en dryRun son las que se crearían). */
  valid: number;
  created: number;
  rejected: number;
  /** Filas que no se alcanzaron a procesar por el deadline del commit. */
  notProcessed: number;
  rows: BulkInvitationRowResult[];
}

export interface BulkInvitationsClient {
  /** Descarga la plantilla con el catálogo de roles que el invitador puede asignar en su contexto. */
  downloadTemplate(signal?: AbortSignal): Promise<void>;
  /** Fase 1 (`dryRun: true`) o fase 2 (`dryRun: false`). */
  submit(file: File, options: { dryRun: boolean; signal?: AbortSignal }): Promise<BulkInvitationReport>;
}

/**
 * Sin respuesta: caída de red o tiempo agotado. En el commit significa que NO se sabe qué pasó
 * con las filas, y algunas invitaciones pueden haberse creado.
 */
export class BulkInvitationsNetworkError extends Error {
  readonly kind: "network" | "timeout";
  constructor(kind: "network" | "timeout") {
    super(kind === "timeout" ? "La solicitud tardó demasiado." : "No se pudo conectar con el servidor.");
    this.name = "BulkInvitationsNetworkError";
    this.kind = kind;
  }
}

interface RouteConfig {
  basePath: string;
  /** Parámetro de query que acota el destino cuando quien invita es SuperAdmin. */
  query: Record<string, string | undefined>;
}

/** Perfil Gestor. `targetTenantId` solo lo manda el SuperAdmin (compañía destino). */
export function createSecurityBulkInvitationsClient(
  scope: { targetTenantId?: string } = {},
): BulkInvitationsClient {
  return buildClient({
    basePath: SECURITY_BULK_INVITATIONS_PATH,
    query: { targetTenantId: scope.targetTenantId },
  });
}

/** Perfil OT. `transitOfficeId` solo lo manda el SuperAdmin (organismo destino). */
export function createOtBulkInvitationsClient(
  scope: { transitOfficeId?: string } = {},
): BulkInvitationsClient {
  return buildClient({
    basePath: OT_BULK_INVITATIONS_PATH,
    query: { transitOfficeId: scope.transitOfficeId },
  });
}

function buildClient({ basePath, query }: RouteConfig): BulkInvitationsClient {
  return {
    downloadTemplate: (signal) =>
      downloadFile(`${basePath}/template`, {
        query,
        signal,
        fallbackFilename: "plantilla-invitacion-masiva-usuarios.xlsx",
      }),

    submit: (file, { dryRun, signal }) => postFile(basePath, query, file, dryRun, signal),
  };
}

async function postFile(
  basePath: string,
  query: Record<string, string | undefined>,
  file: File,
  dryRun: boolean,
  callerSignal?: AbortSignal,
): Promise<BulkInvitationReport> {
  const origin =
    sessionAwareBase(API_BASE_URL) || (typeof window !== "undefined" ? window.location.origin : "http://localhost:3000");
  const url = new URL(basePath, origin);
  url.searchParams.set("dryRun", String(dryRun));
  for (const [key, value] of Object.entries(query)) {
    if (value) url.searchParams.set(key, value);
  }

  const formData = new FormData();
  formData.append("file", file);

  const token = getToken();
  const headers: Record<string, string> = {};
  if (token) headers.Authorization = `Bearer ${token}`;

  // Señal propia para el tiempo máximo; la del llamador (cambio de contexto, cierre) también aborta.
  const controller = new AbortController();
  let timedOut = false;
  const timer = setTimeout(() => {
    timedOut = true;
    controller.abort();
  }, BULK_INVITE_REQUEST_TIMEOUT_MS);
  const onCallerAbort = () => controller.abort();
  if (callerSignal) {
    if (callerSignal.aborted) controller.abort();
    else callerSignal.addEventListener("abort", onCallerAbort, { once: true });
  }

  let response: Response;
  try {
    response = await fetch(url.toString(), { method: "POST", headers, body: formData, signal: controller.signal });
  } catch (err) {
    if (callerSignal?.aborted) throw err; // el llamador ya no espera la respuesta
    throw new BulkInvitationsNetworkError(timedOut ? "timeout" : "network");
  } finally {
    clearTimeout(timer);
    callerSignal?.removeEventListener("abort", onCallerAbort);
  }

  if (!response.ok) {
    let detail: unknown = null;
    try {
      const text = await response.text();
      detail = text ? JSON.parse(text) : null;
    } catch {
      /* error sin cuerpo JSON (p. ej. la página 504 del nginx) */
    }
    throw new ApiError(response.status, friendlyErrorMessage(detail as Record<string, unknown> | null), detail);
  }

  return normalizeBulkInvitationReport(await response.json().catch(() => null), dryRun);
}

// ── Normalización del reporte ──────────────────────────────────────────────────────────────────

function asString(value: unknown): string | null {
  return typeof value === "string" && value.trim() ? value : null;
}

function asCount(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 ? value : null;
}

function normalizeStatus(raw: unknown, errorCode: string | null, dryRun: boolean): BulkInvitationRowStatus {
  const key = typeof raw === "string" ? raw.trim().toLowerCase().replace(/[\s-]/g, "_") : "";
  switch (key) {
    case "valid":
    case "ok":
      return dryRun ? "valid" : "created";
    case "created":
    case "invited":
      return "created";
    case "rejected":
    case "invalid":
    case "error":
    case "failed":
      return "rejected";
    case "not_processed":
    case "notprocessed":
    case "skipped":
      return "not_processed";
    default:
      if (errorCode) return "rejected";
      return dryRun ? "valid" : "created";
  }
}

/**
 * Tolerante con la forma exacta de la respuesta: el contrato definitivo lo fija el backend
 * (ADR-0077) y esto evita que una diferencia menor (`items` por `rows`, un conteo ausente) deje la
 * pantalla en blanco. Los conteos que falten se derivan de las filas.
 */
export function normalizeBulkInvitationReport(raw: unknown, dryRun: boolean): BulkInvitationReport {
  const body = (raw && typeof raw === "object" ? raw : {}) as Record<string, unknown>;
  const rawRows = Array.isArray(body.rows) ? body.rows : Array.isArray(body.items) ? body.items : [];

  const rows: BulkInvitationRowResult[] = rawRows.map((item, index) => {
    const r = (item && typeof item === "object" ? item : {}) as Record<string, unknown>;
    const errorCode = asString(r.errorCode) ?? asString(r.code) ?? asString(r.error);
    return {
      rowNumber: typeof r.rowNumber === "number" ? r.rowNumber : index + 2,
      email: asString(r.email),
      fullName: asString(r.fullName) ?? asString(r.name),
      roleName: asString(r.roleName) ?? asString(r.role),
      status: normalizeStatus(r.status, errorCode, dryRun),
      errorCode,
    };
  });

  const count = (status: BulkInvitationRowStatus) => rows.filter((r) => r.status === status).length;
  const rejected = asCount(body.rejected) ?? count("rejected");
  const notProcessed = asCount(body.notProcessed) ?? count("not_processed");
  const created = dryRun ? 0 : (asCount(body.created) ?? count("created"));
  const total = asCount(body.total) ?? rows.length;
  const valid = dryRun
    ? (asCount(body.valid) ?? count("valid"))
    : (asCount(body.valid) ?? created);

  return { dryRun, total, valid, created, rejected, notProcessed, rows };
}
