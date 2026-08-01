import type { ProcedureTypeSummary } from './types/procedure-parametrization';
import type {
  AceptarConsentimientoResult,
  ActorContactLookupInput,
  ActorContactLookupResult,
  ActorsResponse,
  AttachmentsResponse,
  BiometriaPublicView,
  BiometricParte,
  BiometricValidation,
  BiometricValidationsResponse,
  ChecklistView,
  CommercialData,
  SuggestedCommercialValue,
  CompletarBiometriaResult,
  ConsultaVehiculoInput,
  ConsultationProvidersConfig,
  ConsultationResult,
  CreateFromConsultaResult,
  CreateInstanceRequest,
  PreflightPreviewResult,
  DocumentOcrResult,
  PersistOcrFieldsResult,
  EditarPrevalidacionRequest,
  EditarPrevalidacionResult,
  ReenviarPrevalidacionResult,
  EnsureIdentityResult,
  FieldValueInput,
  FinalizarPortalResult,
  GenerarFurResult,
  FurTemplateFormatResult,
  GenerarConsolidadoResult,
  GenerarImprontaAttachmentResult,
  IdentityAuditResponse,
  IdentityValidationAlertsResponse,
  IniciarPrevalidacionRequest,
  IniciarPrevalidacionResult,
  PrendaData,
  PrendaInput,
  InstanceSummary,
  InstancesResponse,
  ListInstancesParams,
  TransitOfficeOption,
  TransitOfficesResponse,
  IniciarBiometriaInput,
  IniciarBiometriaResult,
  InvitarParticipanteInput,
  InvitarParticipanteResult,
  Participant,
  ParticipantsResponse,
  PortalFirmaUrl,
  PortalView,
  FineDetail,
  PreflightSnapshot,
  PresignAttachmentResponse,
  ProcedureActor,
  ProcedureAttachment,
  ProcedureConfiguration,
  ProcedureInstanceDetail,
  ReconcileIdentityResult,
  ProcedureInstanceSummary,
  CompletePlateFlowResult,
  RuntPersonLookupInput,
  RuntPersonLookupResult,
  ValidateSoatResult,
  RuesPersonLookupInput,
  RuesPersonLookupResult,
  ActiveDeed,
  LegalRepresentativeLookupResult,
  Signature,
  SignaturesResponse,
  SimularFirmaResult,
  SolicitarFirmaInput,
  StatusHistoryPage,
  TenantBiometricValidationsResponse,
  TenantBiometricValidationFilters,
  StuckIdentityValidationsResponse,
  WizardModalidad,
  WizardState,
} from './types/procedure-runtime';

/**
 * Espejo del PreflightSnapshotDto del backend. Forma casi idéntica a
 * PreflightSnapshot (UI) salvo `provider` y que `message` puede faltar;
 * se mapea a PreflightSnapshot para reusar el PreflightPanel existente.
 */
interface PreflightSnapshotDto {
  overall: PreflightSnapshot['overall'];
  checks: Array<{
    key: string;
    label: string;
    status: PreflightSnapshot['checks'][number]['status'];
    source: string;
    message?: string;
    details?: FineDetail[] | null;
  }>;
  provider?: string;
  createdAt: string;
}

/** Espejo del PreflightPreviewDto del backend (CF-02): snapshot del paso 1 + token de reúso. */
interface PreflightPreviewDto extends PreflightSnapshotDto {
  previewToken: string;
  vehicleFields?: Array<{ fieldKey: string; valueText?: string | null; valueJson?: string | null }>;
}

function mapChecks(dtos: PreflightSnapshotDto['checks']): PreflightSnapshot['checks'] {
  return dtos.map((c) => ({
    key: c.key,
    label: c.label,
    status: c.status,
    source: c.source,
    message: c.message ?? '',
    details: c.details ?? null,
  }));
}

function mapPreflight(dto: PreflightSnapshotDto): PreflightSnapshot {
  return {
    overall: dto.overall,
    checks: mapChecks(dto.checks),
    createdAt: dto.createdAt,
  };
}
import { DEV_TENANT_ID, DEV_USER_ID } from './dev-constants';
import { getToken } from './client';
import { decodeJwtPayload } from '@/lib/auth/jwt';
import { buildListInstancesSearchParams } from '@/lib/tramites/list-instances-query';

export { DEV_TENANT_ID, DEV_USER_ID };

// La API vive en otro origen (api.<env>.flitsas.online); el CD inyecta
// NEXT_PUBLIC_API_BASE_URL (la MISMA variable que usa lib/api/client.ts). Sin variable
// en dev local, las peticiones van al origen del frontend (localhost:3000) y Next.js
// las reescribe a core-api (:4003) vía next.config.ts — no hace falta levantar el gateway.
const BASE_URL =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? process.env.NEXT_PUBLIC_API_URL ?? '';

// Único constructor de URLs del cliente. El path absoluto (/api/v1/...) toma solo el
// ORIGEN de BASE_URL e ignora su path, así un BASE_URL con sufijo /api/v1 (el que inyecta
// el CD) NO se duplica (`…/api/v1/api/v1/…` → 404). Mismo patrón que lib/api/client.ts.
// Usarlo SIEMPRE; no concatenar `${BASE_URL}${path}` (rompe cuando el base trae sufijo).
export const apiUrl = (path: string): string => {
  const base =
    BASE_URL ||
    (typeof window !== 'undefined' ? window.location.origin : 'http://localhost:3000');
  return new URL(path, base).toString();
};

const JSON_HEADERS: HeadersInit = {
  'Content-Type': 'application/json',
};

/**
 * Returns true only for transient network failures that are safe to retry
 * (ECONNRESET, DNS/TCP drops, generic fetch failure).
 * 4xx/5xx responses are NOT network errors — their messages start with the
 * HTTP status code (e.g. "400 Bad Request") so they never match here.
 */
function isNetworkError(err: unknown): boolean {
  if (!(err instanceof Error)) return false;
  const msg = err.message.toLowerCase();
  return (
    msg.includes('econnreset') ||
    msg.includes('fetch failed') ||
    msg.includes('failed to fetch') ||
    msg.includes('networkerror') ||
    msg.includes('network error') ||
    (msg.includes('network') && !msg.match(/^\d{3}/))
  );
}

/**
 * Executes `fn` and, if it throws a transient network error, waits 300 ms
 * and retries exactly once. Non-network errors (4xx/5xx) propagate immediately.
 */
async function withRetry<T>(fn: () => Promise<T>): Promise<T> {
  try {
    return await fn();
  } catch (err) {
    if (!isNetworkError(err)) throw err;
    await new Promise<void>((resolve) => setTimeout(resolve, 300));
    return fn();
  }
}

/**
 * Mensaje de error legible a partir de una respuesta fallida. Si el cuerpo es un
 * ProblemDetails (RFC 7807) con `detail`/`title` (lo que responde nuestra API), usa ese texto
 * tal cual (p. ej. "La compañía no tiene habilitada la matrícula inicial."). Si NO es JSON
 * (p. ej. el HTML crudo de un gateway/ingress ante un 502/503/504) NUNCA se vuelca al usuario:
 * se traduce a un mensaje amigable según el tipo de fallo.
 */
function problemMessage(res: Response, body: string): string {
  if (body) {
    try {
      const problem = JSON.parse(body) as { detail?: string; title?: string };
      const msg = problem.detail || problem.title;
      if (msg) return msg;
    } catch {
      // body no es JSON (HTML de gateway, texto plano) → mensaje amigable abajo.
    }
  }
  // Servicio no disponible / timeout de gateway: sin conexión (status 0) o 502/503/504.
  if (res.status === 0 || res.status === 502 || res.status === 503 || res.status === 504)
    return 'El servicio no está disponible en este momento. Vuelve a intentarlo en unos minutos.';
  if (res.status >= 500)
    return 'Ocurrió un error en el servidor. Vuelve a intentarlo en unos minutos.';
  return 'No se pudo completar la solicitud. Revisa los datos e inténtalo de nuevo.';
}

/**
 * Cuerpo ProblemDetails (RFC 7807) parseado, si la respuesta de error trae JSON. `title` viaja
 * como código de error en varios endpoints de trámites (p. ej. `DUPLICATE_ACTIVE_PROCEDURE`); las
 * `extensions` del backend (p. ej. `procedureInstanceId`) se serializan como miembros adicionales
 * a nivel raíz.
 */
function parseProblem(body: string): Record<string, unknown> | null {
  if (!body) return null;
  try {
    const parsed = JSON.parse(body) as unknown;
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}

/**
 * Error de una llamada a la API de trámites: conserva el `status` HTTP y el ProblemDetails
 * parseado (`problem`) además del mensaje legible que ya consumen los callers existentes
 * (`err.message`, vía `err instanceof Error`). Los callers que solo necesitan el mensaje siguen
 * funcionando sin cambios; los que necesitan reaccionar a un código de error concreto (p. ej. AC1
 * de HU #10882, 409 `DUPLICATE_ACTIVE_PROCEDURE`) leen `.status` / `.problem`.
 */
export class TramitesApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly problem: Record<string, unknown> | null,
  ) {
    super(message);
    this.name = 'TramitesApiError';
  }
}

/**
 * AC1 (HU #10882) — detecta el bloqueo de duplicidad de trámite en curso (409
 * `DUPLICATE_ACTIVE_PROCEDURE`, HU #10876) que puede devolver el preflight de consulta de
 * vehículo y extrae el id del trámite existente para ofrecer "Retomar" (AC2). Devuelve `null`
 * para cualquier otro error (incluidos otros 409, p. ej. el de creación por tipo no publicado).
 *
 * Duck-typing sobre `{ status, problem }` (en vez de `instanceof TramitesApiError`): la función
 * queda desacoplada de la identidad exacta de la clase, así sigue funcionando igual sobre
 * cualquier error con esa forma (p. ej. en tests que mockean `@/lib/api/tramites-client`).
 */
export function getDuplicateActiveProcedureId(err: unknown): string | null {
  if (!err || typeof err !== 'object') return null;
  const { status, problem } = err as { status?: unknown; problem?: unknown };
  if (status !== 409 || !problem || typeof problem !== 'object') return null;
  const { title, procedureInstanceId } = problem as { title?: unknown; procedureInstanceId?: unknown };
  if (title !== 'DUPLICATE_ACTIVE_PROCEDURE' || typeof procedureInstanceId !== 'string') return null;
  return procedureInstanceId;
}

/** Detalle del bloqueo registral CF-03 (HU #10877) extraído de las extensions RFC7807 del 422. */
export interface VehicleStateBlockInfo {
  vehicleStatus: string;
  procedureType: string;
}

/**
 * AC1/AC2 (HU #10884) — detecta el bloqueo DURO "vehículo ya matriculado" (422
 * `VEHICLE_STATE_INVALID_FOR_TYPE`, CF-03 de HU #10877) que puede devolver el preflight de
 * consulta de vehículo y extrae `vehicleStatus`/`procedureType` para diferenciar el mensaje:
 * `ACTIVO` (el RUNT reporta el vehículo ya matriculado) y `APROBADO_FLIT` (ya existe una
 * matrícula APROBADA en FLIT para el mismo VIN) ⇒ AC1 "ya matriculado"; `DESCONOCIDO` (el RUNT no
 * respondió o no trajo el dato) ⇒ AC2 "RUNT sin dato". A diferencia del check informativo (HU
 * #10538) o del "riesgo aceptado" sobre un fail clásico de `estado_vehiculo`, este bloqueo NO es
 * subsanable: no se ofrece continuar.
 *
 * Duck-typing sobre `{ status, problem }`, mismo patrón que `getDuplicateActiveProcedureId`.
 */
export function getVehicleStateBlock(err: unknown): VehicleStateBlockInfo | null {
  if (!err || typeof err !== 'object') return null;
  const { status, problem } = err as { status?: unknown; problem?: unknown };
  if (status !== 422 || !problem || typeof problem !== 'object') return null;
  const { title, vehicleStatus, procedureType } = problem as {
    title?: unknown;
    vehicleStatus?: unknown;
    procedureType?: unknown;
  };
  if (title !== 'VEHICLE_STATE_INVALID_FOR_TYPE' || typeof vehicleStatus !== 'string') return null;
  return { vehicleStatus, procedureType: typeof procedureType === 'string' ? procedureType : '' };
}

// Exportado para que otros clientes del mismo dominio (p. ej. lib/api/ui-preferences.ts)
// reutilicen el mismo manejo de errores/JSON en vez de reimplementarlo.
export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = getToken();
  const res = await fetch(apiUrl(path), {
    ...init,
    headers: {
      ...JSON_HEADERS,
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init?.headers,
    },
  });
  if (!res.ok) {
    const body = await res.text().catch(() => '');
    throw new TramitesApiError(res.status, problemMessage(res, body), parseProblem(body));
  }

  if (res.status === 204) {
    return undefined as T;
  }

  const contentLength = res.headers.get('content-length');
  if (contentLength === '0') {
    return undefined as T;
  }

  const text = await res.text();
  if (!text.trim()) {
    return undefined as T;
  }

  return JSON.parse(text) as T;
}

/**
 * Tenant "activo" para llamadas per-instance (#1). El backend deriva el tenant del JWT para
 * usuarios de compañía; un SuperAdmin que abre el trámite de OTRA compañía fija aquí el tenant de
 * esa fila para que las llamadas per-instance lo lleven en X-Tenant-Id. Lo setea la página del
 * wizard desde el query param `?t=` (ver app/tramites/[instanceId]).
 */
let activeTramitesTenant: string | undefined;

/** Fija (o limpia) el tenant activo para las llamadas per-instance de trámites. */
export function setActiveTramitesTenant(tenantId: string | undefined): void {
  activeTramitesTenant = tenantId;
}

/** tenant_id del JWT en cookie (company-user). `undefined` si no hay token o claim. */
function jwtTenantId(): string | undefined {
  return decodeJwtPayload(getToken())?.tenant_id ?? undefined;
}

/**
 * Headers de runtime: Bearer del JWT + X-Tenant-Id resuelto. La resolución del tenant es:
 * explícito → tenant activo (superadmin abriendo otra compañía) → tenant del JWT (company-user).
 * Para un company-user el backend igual lo sobrescribe desde el token (defensa); enviarlo solo
 * mantiene la llamada coherente. NO es el header X-Flit-SuperAdmin de parametrización.
 */
// Exportado por el mismo motivo que `request`: es el único lugar que resuelve Bearer +
// X-Tenant-Id (explícito → tenant activo → JWT), y otros clientes (ui-preferences.ts) lo
// necesitan tal cual, sin duplicar la resolución de tenant.
export function tenantHeader(tenantId?: string): HeadersInit {
  const headers: Record<string, string> = {};
  const token = getToken();
  if (token) headers.Authorization = `Bearer ${token}`;
  const resolved = tenantId ?? activeTramitesTenant ?? jwtTenantId();
  if (resolved) headers['X-Tenant-Id'] = resolved;
  return headers;
}

/**
 * SHA-256 del archivo en hex minúsculas. En la subida directa a S3 el binario no pasa por el API,
 * así que el hash de integridad lo calcula el navegador (Web Crypto, requiere contexto seguro:
 * https o localhost) y se envía al registrar la metadata del adjunto.
 */
async function sha256Hex(file: File): Promise<string> {
  const buffer = await file.arrayBuffer();
  const digest = await crypto.subtle.digest('SHA-256', buffer);
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('');
}

export const tramitesClient = {
  // AC1 — selector solo published. Sin header de tenant.
  listPublishedProcedureTypes: () =>
    request<ProcedureTypeSummary[]>(
      '/api/v1/tramites/procedure-types?publicationStatus=published',
    ),

  // AC2 — config pública por code (string).
  getConfiguration: (code: string) =>
    request<ProcedureConfiguration>(
      `/api/v1/procedure-types/${encodeURIComponent(code)}/configuration`,
    ),

  // POST create — tenant viaja en el BODY (inconsistencia documentada del contrato).
  createInstance: (body: CreateInstanceRequest) =>
    request<ProcedureInstanceSummary>('/api/v1/tramites/instances', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // Slice M6 — listado de instancias para la tabla "Trámites en curso".
  // GET devuelve { items, total? }; se desempaqueta al arreglo para el consumidor.
  // #1 — El tenant lo deriva el backend del JWT: company-user ve solo su compañía. El SuperAdmin
  // ve TODO; solo se manda X-Tenant-Id si elige una compañía (filterTenantId).
  // Acepta string legacy (= filterTenantId) o un objeto con filtros/orden server-side.
  listInstances: async (
    filterTenantIdOrParams?: string | ListInstancesParams,
  ): Promise<InstanceSummary[]> => {
    const params: ListInstancesParams =
      typeof filterTenantIdOrParams === 'string'
        ? { filterTenantId: filterTenantIdOrParams }
        : (filterTenantIdOrParams ?? {});

    const headers: Record<string, string> = {};
    if (params.filterTenantId) headers['X-Tenant-Id'] = params.filterTenantId;

    const { filterTenantId: _tenant, ...query } = params;
    const qs = buildListInstancesSearchParams(query).toString();
    const path = qs
      ? `/api/v1/tramites/instances?${qs}`
      : '/api/v1/tramites/instances';

    const res = await request<InstancesResponse>(path, { headers });
    // Normaliza los campos async de HU #10350 con defaults seguros: un backend que aún no los
    // exponga (transición) deja la tabla funcionando (chips/estado base) sin romper el render.
    return (res?.items ?? []).map((item) => ({
      ...item,
      draftFinalizedAt: item.draftFinalizedAt ?? null,
      identityValidationStatus: item.identityValidationStatus ?? null,
      signaturePending: item.signaturePending ?? false,
      canSubmit: item.canSubmit ?? false,
      prioritario: item.prioritario ?? false,
      // HU #11056 — mismo criterio: un backend que aún no exponga estas columnas deja la tabla
      // funcionando. `fuente` cae a 'dashboard' (el origen por defecto), y los estados de "Firmado" a
      // null = "no aplica", que es la lectura conservadora: no inventa un estado que no se conoce.
      updatedAt: item.updatedAt ?? null,
      gestorNombre: item.gestorNombre ?? null,
      fuente: item.fuente ?? 'dashboard',
      firmaVendedorEstado: item.firmaVendedorEstado ?? null,
      firmaCompradorEstado: item.firmaCompradorEstado ?? null,
      consolidadoAttachmentId: item.consolidadoAttachmentId ?? null,
    }));
  },

  // HU #10536 — marca/desmarca el trámite como prioritario (el OT lo revisa con primacía).
  // No cambia el estado del ciclo de vida; solo el flag de ordenamiento de los listados.
  setPriority: (id: string, prioritario: boolean, tenantId?: string) =>
    request<{ id: string; prioritario: boolean }>(
      `/api/v1/tramites/instances/${id}/priority`,
      {
        method: 'PATCH',
        headers: tenantHeader(tenantId),
        body: JSON.stringify({ prioritario }),
      },
    ),

  // ICT (paridad v1 handleChangePausedState) — pausar/reanudar un trámite ICT (solo borradores
  // origin='ict'). No cambia el estado del ciclo de vida; un trámite pausado no radica (guard 409 en submit).
  pauseInstance: (
    id: string,
    paused: boolean,
    observation?: string | null,
    tenantId?: string,
  ) =>
    request<{ id: string; isPaused: boolean; pausedObservation: string | null }>(
      `/api/v1/tramites/instances/${id}/pause`,
      {
        method: 'PUT',
        headers: tenantHeader(tenantId),
        body: JSON.stringify({ paused, observation: observation ?? null }),
      },
    ),

  // ICT (paridad v1 pause-unpause-massive) — pausar/reanudar en lote. Devuelve el detalle por trámite.
  pauseInstancesMassive: (
    ids: string[],
    paused: boolean,
    observation?: string | null,
    tenantId?: string,
  ) =>
    request<{
      total: number;
      processed: number;
      detail: { id: string; ok: boolean; error: string | null }[];
    }>(`/api/v1/tramites/instances/pause-massive`, {
      method: 'POST',
      headers: tenantHeader(tenantId),
      body: JSON.stringify({ ids, paused, observation: observation ?? null }),
    }),

  // #2 — Organismos de tránsito habilitados para la empresa (tenant del header).
  // El operador solo puede elegir/enviar a estos en el FUR.
  listTransitOffices: async (
    tenantId?: string,
  ): Promise<TransitOfficeOption[]> => {
    const res = await request<TransitOfficesResponse>(
      '/api/v1/tramites/transit-offices',
      { headers: tenantHeader(tenantId) },
    );
    return res?.items ?? [];
  },

  getInstance: (id: string, tenantId?: string) =>
    request<ProcedureInstanceDetail>(`/api/v1/tramites/instances/${id}`, {
      headers: tenantHeader(tenantId),
    }),

  // AC3 — guardar borrador (409 not_draft si ya enviada).
  patchFieldValues: (
    id: string,
    items: FieldValueInput[],
    tenantId?: string,
  ) =>
    request<ProcedureInstanceDetail>(
      `/api/v1/tramites/instances/${id}/field-values`,
      {
        method: 'PATCH',
        headers: tenantHeader(tenantId),
        body: JSON.stringify({ items }),
      },
    ),

  submitInstance: (id: string, tenantId?: string) =>
    request<ProcedureInstanceSummary>(
      `/api/v1/tramites/instances/${id}/submit`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
      },
    ),

  // HU #10350 (AC1) — finalizar el borrador: sella draftFinalizedAt SIN exigir identidad/FUR. El
  // trámite permanece en `draft`; la firma se dispara async cuando el cliente valida su identidad.
  // Distinto de submit (que sí radica a tránsito y exige identidad + gates completos).
  // 409 si la instancia no es draft o faltan datos (actores/documentos/organismo).
  finalizeDraft: (id: string, tenantId?: string) =>
    request<ProcedureInstanceSummary>(
      `/api/v1/tramites/instances/${id}/finalize-draft`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
      },
    ),

  // Slice 2 — actores del trámite. GET devuelve { actors }; se desempaqueta
  // para que los consumidores reciban directamente el arreglo.
  getActors: async (
    instanceId: string,
    tenantId?: string,
  ): Promise<ProcedureActor[]> => {
    const res = await request<ActorsResponse>(
      `/api/v1/tramites/instances/${instanceId}/actors`,
      { headers: tenantHeader(tenantId) },
    );
    return res?.actors ?? [];
  },

  // PUT set completo de actores (reemplaza el conjunto guardado).
  // withRetry: 1 reintento tras 300 ms solo si el error es de red (ECONNRESET /
  // fetch failed / network). Errores 4xx/5xx se propagan sin reintentar.
  saveActors: (
    instanceId: string,
    actors: ProcedureActor[],
    tenantId?: string,
  ) =>
    withRetry(() =>
      request<void>(`/api/v1/tramites/instances/${instanceId}/actors`, {
        method: 'PUT',
        headers: tenantHeader(tenantId),
        body: JSON.stringify({ actors }),
      }),
    ),

  // Slice M3 — autopopulado del actor desde RUNT por documento. Siempre 200
  // ante petición válida; `found=false` => fallback manual (no bloquea captura).
  runtPersonLookup: (
    instanceId: string,
    input: RuntPersonLookupInput,
    tenantId?: string,
  ) =>
    request<RuntPersonLookupResult>(
      `/api/v1/tramites/instances/${instanceId}/runt-person`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
        body: JSON.stringify(input),
      },
    ),

  // Autopopulado JURÍDICO del actor desde RUES por NIT (bifurcación del "Consultar RUNT" para
  // persona jurídica). Siempre 200 ante petición válida; `found=false` => fallback manual.
  ruesPersonLookup: (
    instanceId: string,
    input: RuesPersonLookupInput,
    tenantId?: string,
  ) =>
    request<RuesPersonLookupResult>(
      `/api/v1/tramites/instances/${instanceId}/rues-lookup`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
        body: JSON.stringify(input),
      },
    ),

  // HU #10956 (revierte parcialmente HU #10885/#10878, AC2/AC3/AC4/AC5) — precarga SOLO datos de
  // CONTACTO (ciudad/correo/dirección/teléfono) de una persona ya conocida en el tenant, tras
  // resolver su identidad en vivo (RUNT/RUES/directorio). No es un lookup por instancia (no lleva
  // `instanceId` en la ruta): el actor más reciente de esa persona puede venir de CUALQUIER trámite
  // del tenant. Siempre 200; sin antecedentes responde los 4 campos en null (AC4), nunca 404.
  actorContactLookup: (
    input: ActorContactLookupInput,
    tenantId?: string,
  ) =>
    request<ActorContactLookupResult>(
      `/api/v1/tramites/actors/contact-lookup?tipoDocumento=${encodeURIComponent(input.tipoDocumento)}&numeroDocumento=${encodeURIComponent(input.numeroDocumento)}`,
      { headers: tenantHeader(tenantId) },
    ),

  // HU #10903/#10906 — escrituras activas y VIGENTES del tenant, para el collapse del primer paso del
  // wizard. Tenant-scoped por el header X-Tenant-Id (el backend lo impone desde el JWT; un SuperAdmin
  // acota con el tenant activo). GET devuelve { items }; se desempaqueta al arreglo (default seguro).
  fetchActiveDeeds: async (tenantId?: string): Promise<ActiveDeed[]> => {
    const res = await request<{ items: ActiveDeed[] }>(
      '/api/v1/tramites/deeds/active',
      { headers: tenantHeader(tenantId) },
    );
    return res?.items ?? [];
  },

  // HU #10903/#10906 — precarga comprador/vendedor por NIT desde el directorio del tenant. 200 con el
  // match (compañía + representante + firma/identidad vigentes) o 404 → null (el FE cae a RUES/RUNT).
  // Fetch crudo para distinguir el 404 "sin match" (esperado) de un error real; NO usa request()
  // porque su mensaje de error no expone el status para diferenciar el 404.
  lookupLegalRepresentativeByNit: async (
    nit: string,
    tenantId?: string,
  ): Promise<LegalRepresentativeLookupResult | null> => {
    const res = await fetch(
      apiUrl(`/api/v1/tramites/legal-representatives/lookup?nit=${encodeURIComponent(nit)}`),
      { headers: tenantHeader(tenantId) },
    );
    if (res.status === 404) return null;
    if (!res.ok) {
      const body = await res.text().catch(() => '');
      throw new Error(problemMessage(res, body));
    }
    const text = await res.text();
    return text.trim()
      ? (JSON.parse(text) as LegalRepresentativeLookupResult)
      : null;
  },

  // HU #10478 — proveedor primario de consulta resuelto para el tenant (por tipo). El wizard lo
  // consulta para adaptar la UI (ocultar el tipo de documento del propietario si el proveedor de
  // placa es Kyverum RUNT, que lo resuelve solo).
  getConsultationConfig: (tenantId?: string) =>
    request<ConsultationProvidersConfig>(
      `/api/v1/tramites/consultation-config`,
      { headers: tenantHeader(tenantId) },
    ),

  // #10201 — consulta real de fuentes externas (RUNT/SIMIT). Mapea
  // ConsultationResult del backend al shape PreflightSnapshot del panel.
  // HU #10885 (Feature #10862, CF-04): `forceRefresh` (AC2, botón "Actualizar") viaja como query
  // param opcional — default false (cero regresión) — y salta el reúso de caché en el backend
  // (ADR-0030). `fromCache`/`queriedAt` (AC1) viajan tal cual del DTO al PreflightSnapshot.
  runConsultation: async (
    instanceId: string,
    templateCode: string,
    tenantId?: string,
    forceRefresh = false,
  ): Promise<PreflightSnapshot> => {
    const query = forceRefresh ? '?forceRefresh=true' : '';
    const result = await request<ConsultationResult>(
      `/api/v1/tramites/instances/${instanceId}/consultations/${templateCode}${query}`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
      },
    );
    return {
      overall: result.overall,
      checks: result.checks.map((c) => ({
        key: c.key,
        label: c.label,
        status: c.status,
        source: c.source,
        message: c.message ?? '',
      })),
      createdAt: new Date().toISOString(),
      fromCache: result.fromCache ?? false,
      queriedAt: result.queriedAt ?? null,
    };
  },

  // HU #10611 (Feature #10587) — valida el SOAT re-consultando el RUNT del vehículo con el trámite
  // en 'asignado'. El backend marca soat_estado (vigente/vencido/unknown) sin cambiar de estado.
  validateSoatViaRunt: (instanceId: string, tenantId?: string) =>
    request<ValidateSoatResult>(
      `/api/v1/tramites/instances/${instanceId}/soat/validate-runt`,
      { method: 'POST', headers: tenantHeader(tenantId) },
    ),

  /**
   * Gestor en Asignado: checks opcionales + avanza a Terminado.
   *
   * El trámite puede avanzar CON salvedades (p. ej. la compañía permite continuar sin SOAT vigente):
   * en ese caso llega `warningMessage` y la UI debe mostrarlo aunque la operación haya salido bien.
   */
  completePlateFlow: (
    instanceId: string,
    body: { soatPagado?: boolean; impuestoDepartamentalPagado?: boolean } = {},
    tenantId?: string,
  ) =>
    request<CompletePlateFlowResult>(
      `/api/v1/tramites/instances/${instanceId}/plate-flow/complete`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
        body: JSON.stringify(body),
      },
    ),

  // ── Documentos / checklist (Slice 3) ────────────────────────────
  // Checklist guiado por la tipología: qué docTipos exige el trámite y
  // cuáles ya están satisfechos.
  getChecklist: (instanceId: string, tenantId?: string) =>
    request<ChecklistView>(
      `/api/v1/tramites/instances/${instanceId}/checklist`,
      { headers: tenantHeader(tenantId) },
    ),

  // GET adjuntos. Devuelve { attachments }; se desempaqueta al arreglo.
  getAttachments: async (
    instanceId: string,
    tenantId?: string,
  ): Promise<ProcedureAttachment[]> => {
    const res = await request<AttachmentsResponse>(
      `/api/v1/tramites/instances/${instanceId}/attachments`,
      { headers: tenantHeader(tenantId) },
    );
    return res?.attachments ?? [];
  },

  // OCR semántico de un documento ANTES de subirlo al expediente. Multipart POST a través del API
  // (a diferencia de uploadAttachment, que sube el binario directo a S3). Devuelve el JSON extraído y,
  // en PDFs multi-documento, el recorte en base64. Lanza si la respuesta no es OK (proveedor caído/
  // timeout/tipo o archivo inválido) → el hook aborta la subida y ofrece carga manual.
  analyzeDocument: async (
    tipo: string,
    file: File,
    tenantId?: string,
  ): Promise<DocumentOcrResult> => {
    const form = new FormData();
    form.append('file', file);
    // Sin Content-Type manual: el navegador fija el boundary del multipart. tenantHeader añade Bearer + X-Tenant-Id.
    const res = await fetch(
      apiUrl(`/api/v1/tramites/ocr/${encodeURIComponent(tipo)}`),
      { method: 'POST', headers: tenantHeader(tenantId), body: form },
    );
    if (!res.ok) {
      const body = await res.text().catch(() => '');
      throw new Error(problemMessage(res, body));
    }
    return JSON.parse(await res.text()) as DocumentOcrResult;
  },

  /**
   * HU #10975 (Feature #10972) — persiste en `field_values` los campos que el OCR ya extrajo del
   * documento (p. ej. número de póliza y fechas del SOAT), que antes se pintaban en el panel de
   * validación y se descartaban. El backend aplica su propia whitelist por tipo y la regla de
   * precedencia (el dato de una consulta al RUNT manda sobre el de un PDF), así que aquí se manda
   * el JSON del OCR tal cual.
   */
  persistOcrFields: async (
    instanceId: string,
    tipo: string,
    fields: Record<string, unknown>,
    tenantId?: string,
  ): Promise<PersistOcrFieldsResult> => {
    // Solo los escalares de texto/número interesan: el backend descarta lo que no esté en su
    // whitelist, pero enviar arrays/objetos (paginas_documento, alertas…) solo infla el request.
    const planos: Record<string, string> = {};
    for (const [k, v] of Object.entries(fields)) {
      if (typeof v === 'string' && v.trim() !== '') planos[k] = v.trim();
      else if (typeof v === 'number') planos[k] = String(v);
    }

    const res = await fetch(
      apiUrl(`/api/v1/tramites/instances/${instanceId}/ocr-fields`),
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...tenantHeader(tenantId) },
        body: JSON.stringify({ tipo, fields: planos }),
      },
    );
    if (!res.ok) {
      const body = await res.text().catch(() => '');
      throw new Error(problemMessage(res, body));
    }
    return JSON.parse(await res.text()) as PersistOcrFieldsResult;
  },

  // Subida directa navegador→S3 (presigned). El binario NO pasa por el request del
  // API (resuelve PDFs grandes que fallaban en el límite del request/gateway):
  //   1) presign  → el API registra el archivo en el file-manager y devuelve la POST policy de S3.
  //   2) POST a S3 → el navegador sube el binario directo con los campos firmados + el archivo.
  //   3) register → el API persiste la metadata del adjunto (incl. el sha256 que calcula el cliente).
  uploadAttachment: async (
    instanceId: string,
    tipo: string,
    file: File,
    tenantId?: string,
  ): Promise<ProcedureAttachment> => {
    const mimetype = file.type || 'application/octet-stream';
    const filename = file.name || 'file';
    const sha256 = await sha256Hex(file);

    // 1) presign
    const presign = await request<PresignAttachmentResponse>(
      `/api/v1/tramites/instances/${instanceId}/attachments/presign`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
        body: JSON.stringify({ tipo, filename, mimetype, sizeBytes: file.size }),
      },
    );

    // 2) POST policy a S3: los campos firmados van ANTES del 'file'. NO se fija Content-Type ni
    // headers de tenant: es S3, no el API; el navegador pone el boundary del multipart.
    const form = new FormData();
    for (const [key, value] of Object.entries(presign.fields)) {
      form.append(key, value);
    }
    form.append('file', file);
    const s3Res = await fetch(presign.url, { method: 'POST', body: form });
    if (!s3Res.ok) {
      const body = await s3Res.text().catch(() => '');
      throw new Error(
        `Error subiendo a almacenamiento (${s3Res.status})${body ? ': ' + body : ''}`,
      );
    }

    // 3) register
    return request<ProcedureAttachment>(
      `/api/v1/tramites/instances/${instanceId}/attachments/register`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
        body: JSON.stringify({
          tipo,
          filename,
          mimetype,
          sizeBytes: file.size,
          sha256,
          storagePath: presign.storagePath,
        }),
      },
    );
  },

  // GET descarga binaria de un adjunto. Devuelve el blob + filename/mimetype
  // (resueltos del Content-Disposition / Content-Type de la respuesta) para que
  // el consumidor dispare la descarga del navegador (blob → objectURL → anchor).
  downloadAttachment: async (
    instanceId: string,
    attachmentId: string,
    tenantId?: string,
    fallbackFilename?: string,
  ): Promise<{ blob: Blob; filename: string; mimetype: string }> => {
    const res = await fetch(
      apiUrl(`/api/v1/tramites/instances/${instanceId}/attachments/${attachmentId}/download`),
      { headers: tenantHeader(tenantId) },
    );
    if (!res.ok) {
      const body = await res.text().catch(() => '');
      throw new Error(
        `${res.status} ${res.statusText}${body ? ': ' + body : ''}`,
      );
    }
    const blob = await res.blob();
    const mimetype =
      res.headers.get('content-type') ?? 'application/octet-stream';
    // Content-Disposition: attachment; filename="fur.txt"  (o filename*=UTF-8'')
    const cd = res.headers.get('content-disposition') ?? '';
    const star = /filename\*=(?:UTF-8'')?([^;]+)/i.exec(cd);
    const plain = /filename="?([^";]+)"?/i.exec(cd);
    const raw = star?.[1] ?? plain?.[1] ?? '';
    let filename = raw.trim();
    try {
      filename = raw ? decodeURIComponent(raw.trim()) : '';
    } catch {
      // raw no era URI-encoded; se usa tal cual.
    }
    return { blob, filename: filename || fallbackFilename || attachmentId, mimetype };
  },

  // GET URL presignada de previsualización inline (ADR-0029). TTL ~10 min.
  // El backend valida tenant + ownership antes de emitir { url, expiresAt }.
  fetchAttachmentPreviewUrl: (
    instanceId: string,
    attachmentId: string,
    tenantId?: string,
  ) =>
    request<{ url: string; expiresAt: string }>(
      `/api/v1/tramites/instances/${instanceId}/attachments/${attachmentId}/preview-url`,
      { headers: tenantHeader(tenantId) },
    ),

  // DELETE adjunto -> 204.
  deleteAttachment: (
    instanceId: string,
    attachmentId: string,
    tenantId?: string,
  ) =>
    request<void>(
      `/api/v1/tramites/instances/${instanceId}/attachments/${attachmentId}`,
      {
        method: 'DELETE',
        headers: tenantHeader(tenantId),
      },
    ),

  // ── Wizard server-driven (Slice 4b) ─────────────────────────────
  // El backend decide modalidad, pasos, status, razones y blockers.
  getWizardState: (instanceId: string, tenantId?: string) =>
    request<WizardState>(
      `/api/v1/tramites/instances/${instanceId}/wizard`,
      { headers: tenantHeader(tenantId) },
    ),

  // ── Preflight (semáforo legal) — Slice 4b/5 ─────────────────────
  // POST corre la consulta; GET trae el último snapshot. Ambos mapean
  // al shape PreflightSnapshot que consume el PreflightPanel.
  runPreflight: async (
    instanceId: string,
    tenantId?: string,
  ): Promise<PreflightSnapshot> => {
    const dto = await request<PreflightSnapshotDto>(
      `/api/v1/tramites/instances/${instanceId}/preflight`,
      { method: 'POST', headers: tenantHeader(tenantId) },
    );
    return mapPreflight(dto);
  },

  // CF-02 (HU #10879 AC3 / #10883 AC3) — consulta del vehículo del PASO 1 SIN crear el trámite.
  // Devuelve el mismo semáforo que el preflight de una instancia (y los mismos bloqueos 409/422),
  // más el token con el que la creación posterior reusa esta consulta.
  runPreflightPreview: async (
    input: ConsultaVehiculoInput,
    tenantId?: string,
  ): Promise<PreflightPreviewResult> => {
    const dto = await request<PreflightPreviewDto>('/api/v1/tramites/preflight-preview', {
      method: 'POST',
      headers: tenantHeader(tenantId),
      body: JSON.stringify({
        tenantId: tenantId ?? jwtTenantId() ?? DEV_TENANT_ID,
        modalidad: input.modalidad,
        vin: input.vin ?? null,
        plate: input.plate ?? null,
        ownerDocumentType: input.ownerDocumentType ?? null,
        ownerDocumentNumber: input.ownerDocumentNumber ?? null,
      }),
    });
    return {
      previewToken: dto.previewToken,
      preflight: mapPreflight(dto),
      vehicleFields: (dto.vehicleFields ?? []).map((f) => ({
        formFieldId: '',
        fieldKey: f.fieldKey,
        valueText: f.valueText ?? null,
        valueJson: f.valueJson ?? null,
        source: 'consultation',
      })),
    };
  },

  // CF-02 (HU #10879 AC5 / #10883 AC4) — crea el trámite AL AVANZAR al paso 2, ya con el vehículo
  // consultado: es el único punto del flujo que da de alta el registro. `previewToken` evita repetir
  // la consulta al proveedor externo; si expiró, el backend consulta de nuevo (no falla).
  createInstanceFromConsulta: async (
    input: ConsultaVehiculoInput & { previewToken?: string | null },
    tenantId?: string,
  ): Promise<CreateFromConsultaResult> => {
    const payload = decodeJwtPayload(getToken());
    const dto = await request<{
      instance: ProcedureInstanceSummary;
      preflight: PreflightSnapshotDto | null;
    }>('/api/v1/tramites/instances/from-consulta', {
      method: 'POST',
      headers: tenantHeader(tenantId),
      body: JSON.stringify({
        tenantId: tenantId ?? payload?.tenant_id ?? DEV_TENANT_ID,
        createdByUserId: payload?.sub ?? DEV_USER_ID,
        modalidad: input.modalidad,
        vin: input.vin ?? null,
        plate: input.plate ?? null,
        ownerDocumentType: input.ownerDocumentType ?? null,
        ownerDocumentNumber: input.ownerDocumentNumber ?? null,
        previewToken: input.previewToken ?? null,
        transitOfficeId: null,
      }),
    });
    return {
      instance: dto.instance,
      preflight: dto.preflight ? mapPreflight(dto.preflight) : null,
    };
  },

  // CF-02 (HU #10883, AC3) — esqueleto de pasos para pintar el wizard en el paso 1 mientras el
  // trámite aún no existe. Mismos pasos/etiquetas que el wizard real, con el resto bloqueado.
  getWizardPreview: (modalidad: WizardModalidad) =>
    request<WizardState>(
      `/api/v1/tramites/wizard-preview?modalidad=${encodeURIComponent(modalidad)}`,
    ),

  // HU #10879/#10883 — autosave del avance del wizard: persiste la `key` del paso donde quedó el
  // operador para retomar ahí al reabrir el borrador (AC2). PATCH /instances/{id}/current-step; el
  // backend valida internamente que el trámite esté en borrador y que la consulta del vehículo ya
  // esté completa (409 en otro caso) — el caller (AC1) trata cualquier fallo como no bloqueante
  // (autosave silencioso, no debe interrumpir la navegación del wizard).
  setCurrentStep: (instanceId: string, step: string, tenantId?: string) =>
    request<{ id: string; currentStep: string | null }>(
      `/api/v1/tramites/instances/${instanceId}/current-step`,
      {
        method: 'PATCH',
        headers: tenantHeader(tenantId),
        body: JSON.stringify({ step }),
      },
    ),

  getPreflight: async (
    instanceId: string,
    tenantId?: string,
  ): Promise<PreflightSnapshot | null> => {
    // DS-4B-3: el 404 significa "sin snapshot todavía" → null explícito.
    // Cualquier otro error (5xx, red) se propaga para no enmascarar fallos.
    let dto: PreflightSnapshotDto | undefined;
    try {
      dto = await request<PreflightSnapshotDto>(
        `/api/v1/tramites/instances/${instanceId}/preflight`,
        { headers: tenantHeader(tenantId) },
      );
    } catch (err) {
      if (err instanceof Error && err.message.startsWith('404')) {
        return null;
      }
      throw err;
    }
    return dto ? mapPreflight(dto) : null;
  },

  // ── RNMC (FEATURE 05) — consulta desacoplada del pre-vuelo ──────
  // POST corre la consulta RNMC por cada actor natural (con su fecha de expedición) y persiste;
  // GET trae el último resultado. Ambos devuelven la lista de checks (rnmc_{rol}_medidas_correctivas).
  runRnmc: async (instanceId: string, tenantId?: string): Promise<PreflightSnapshot['checks']> => {
    const dtos = await request<PreflightSnapshotDto['checks']>(
      `/api/v1/tramites/instances/${instanceId}/rnmc`,
      { method: 'POST', headers: tenantHeader(tenantId) },
    );
    return mapChecks(dtos ?? []);
  },

  getRnmc: async (instanceId: string, tenantId?: string): Promise<PreflightSnapshot['checks']> => {
    const dtos = await request<PreflightSnapshotDto['checks']>(
      `/api/v1/tramites/instances/${instanceId}/rnmc`,
      { headers: tenantHeader(tenantId) },
    );
    return mapChecks(dtos ?? []);
  },

  // ── Datos comerciales (traspaso) — GET/PUT /commercial ──────────
  getCommercial: (instanceId: string, tenantId?: string) =>
    request<CommercialData>(
      `/api/v1/tramites/instances/${instanceId}/commercial`,
      { headers: tenantHeader(tenantId) },
    ),

  putCommercial: (
    instanceId: string,
    data: CommercialData,
    tenantId?: string,
  ) =>
    request<CommercialData>(
      `/api/v1/tramites/instances/${instanceId}/commercial`,
      {
        method: 'PUT',
        headers: tenantHeader(tenantId),
        body: JSON.stringify(data),
      },
    ),

  // ── Avalúo comercial (Feature #10707) — GET /commercial/suggested-value ──
  getSuggestedCommercialValue: (instanceId: string, tenantId?: string) =>
    request<SuggestedCommercialValue>(
      `/api/v1/tramites/instances/${instanceId}/commercial/suggested-value`,
      { headers: tenantHeader(tenantId) },
    ),

  // ── Prenda / gravamen (IT-3, Feature #10585) — GET/PUT /prenda ───
  getPrenda: (instanceId: string, tenantId?: string) =>
    request<PrendaData | null>(
      `/api/v1/tramites/instances/${instanceId}/prenda`,
      { headers: tenantHeader(tenantId) },
    ),

  putPrenda: (instanceId: string, data: PrendaInput, tenantId?: string) =>
    request<PrendaData>(
      `/api/v1/tramites/instances/${instanceId}/prenda`,
      {
        method: 'PUT',
        headers: tenantHeader(tenantId),
        body: JSON.stringify(data),
      },
    ),

  // ── Biométrica (Slice 6) — lado gestor autenticado ──────────────
  // POST iniciar una validación biométrica de una parte. Devuelve el token
  // CRUDO + magicLinkPath (solo aquí) para construir el enlace del participante.
  iniciarBiometric: (
    instanceId: string,
    input: IniciarBiometriaInput,
    tenantId?: string,
  ) =>
    request<IniciarBiometriaResult>(
      `/api/v1/tramites/instances/${instanceId}/biometric`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
        body: JSON.stringify(input),
      },
    ),

  // POST simular la validación biométrica de una parte (mock de esta iteración:
  // la biométrica real es una iteración futura). Devuelve la validación aprobada
  // (estado 'aprobado', score 95). Mismo DTO que listBiometric.
  simulateBiometric: (
    instanceId: string,
    input: { parte: BiometricParte },
    tenantId?: string,
  ) =>
    request<BiometricValidation>(
      `/api/v1/tramites/instances/${instanceId}/biometric/simulate`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
        body: JSON.stringify(input),
      },
    ),

  // HU #10350 — asegura la identidad de una parte al guardarla: el backend reutiliza una validación
  // vigente (≤30 días) de la persona o responde 'requiere_validacion' para que el front la dispare.
  ensureIdentity: (
    instanceId: string,
    parte: BiometricParte,
    tenantId?: string,
  ) =>
    request<EnsureIdentityResult>(
      `/api/v1/tramites/instances/${instanceId}/identity/ensure`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
        body: JSON.stringify({ parte }),
      },
    ),

  // GET lista/estado de las validaciones de la instancia. Desempaqueta a arreglo.
  listBiometric: async (
    instanceId: string,
    tenantId?: string,
  ): Promise<BiometricValidation[]> => {
    const res = await request<BiometricValidationsResponse>(
      `/api/v1/tramites/instances/${instanceId}/biometric`,
      { headers: tenantHeader(tenantId) },
    );
    return res?.validations ?? [];
  },

  /**
   * HU #11014 — igual que `listBiometric` pero conservando la cobertura por firma del baúl, que el
   * expediente necesita para rotular «firmado desde el baúl» en vez de hablar de un certificado de
   * validación de identidad que no existe.
   */
  listBiometricExpediente: async (
    instanceId: string,
    tenantId?: string,
  ): Promise<{ validations: BiometricValidation[]; firmaBaulPartes: string[] }> => {
    const res = await request<BiometricValidationsResponse>(
      `/api/v1/tramites/instances/${instanceId}/biometric`,
      { headers: tenantHeader(tenantId) },
    );
    return {
      validations: res?.validations ?? [],
      firmaBaulPartes: res?.firmaBaulPartes ?? [],
    };
  },

  // HU #10234 — vista transversal del submódulo "Validaciones de Identidad": TODAS las validaciones
  // del tenant + KPIs. No es por-instancia. Devuelve { validations, stats }; default seguro si vacío.
  // HU #10348 — filtros opcionales: se serializan como query params; los vacíos/undefined no se envían
  // (el backend HU #10347 combina con AND y devuelve filas + KPIs del subconjunto filtrado).
  listTenantBiometricValidations: async (
    filters: TenantBiometricValidationFilters = {},
    tenantId?: string,
  ): Promise<TenantBiometricValidationsResponse> => {
    const params = new URLSearchParams();
    const add = (key: string, value: string | number | undefined) => {
      if (value === undefined) return;
      const s = typeof value === 'number' ? String(value) : value.trim();
      if (s !== '') params.set(key, s);
    };
    add('referenceNumber', filters.referenceNumber);
    add('modalidad', filters.modalidad);
    add('name', filters.name);
    add('partyRole', filters.partyRole);
    add('documentType', filters.documentType);
    add('documentNumber', filters.documentNumber);
    add('status', filters.status);
    add('provider', filters.provider);
    add('scoreMin', filters.scoreMin);
    add('scoreMax', filters.scoreMax);
    add('createdFrom', filters.createdFrom);
    add('createdTo', filters.createdTo);
    add('rejectionReason', filters.rejectionReason);
    add('vigenciaEstado', filters.vigenciaEstado);
    add('expiraDesde', filters.expiraDesde);
    add('expiraHasta', filters.expiraHasta);
    add('venceEnDias', filters.venceEnDias);
    add('page', filters.page);
    add('pageSize', filters.pageSize);
    // CF-02 (Feature #11004, HU #11006) — boolean explícito: no reutiliza `add()` (string|number) para
    // no perder `false` (que sí debe viajar como filtro "solo ligadas a trámite").
    if (filters.standalone !== undefined) {
      params.set('standalone', String(filters.standalone));
    }

    const query = params.toString();
    const res = await request<TenantBiometricValidationsResponse>(
      `/api/v1/tramites/biometric-validations${query ? `?${query}` : ''}`,
      { headers: tenantHeader(tenantId) },
    );
    return (
      res ?? {
        validations: [],
        stats: { total: 0, aprobadas: 0, enProceso: 0, rechazadas: 0, expiradas: 0 },
        page: 1,
        pageSize: 20,
        total: 0,
      }
    );
  },

  // HU #10349 (fase 2) — eventos de validación de identidad ATASCADOS (dead-letter): el encadenamiento
  // async (firma/FUR) agotó los reintentos del worker. Para observabilidad + reencolar desde la UI.
  // El tenant se resuelve como el resto del runtime (tenant activo → JWT); NO se hardcodea DEV_TENANT_ID,
  // que mandaba las atascadas de OTRA compañía (el backend ya lo impone desde el token, defensa en fondo).
  listStuckIdentityValidations: async (
    tenantId?: string,
  ): Promise<StuckIdentityValidationsResponse> => {
    const res = await request<StuckIdentityValidationsResponse>(
      '/api/v1/tramites/identity-validation/stuck',
      { headers: tenantHeader(tenantId) },
    );
    return res ?? { stuck: [], total: 0, maxDeliveryAttempts: 5 };
  },

  // POST reencolar ("desatascar") un evento atascado: reinicia sus intentos para que el worker lo retome.
  requeueStuckIdentityValidation: (
    id: string,
    tenantId?: string,
  ): Promise<{ requeued: boolean }> =>
    request<{ requeued: boolean }>(
      `/api/v1/tramites/identity-validation/stuck/${id}/requeue`,
      { method: 'POST', headers: tenantHeader(tenantId) },
    ),

  // POST reencolar TODOS los eventos atascados del tenant de una vez → { requeued: N }.
  requeueAllStuckIdentityValidations: (
    tenantId?: string,
  ): Promise<{ requeued: number }> =>
    request<{ requeued: number }>(
      '/api/v1/tramites/identity-validation/stuck/requeue-all',
      { method: 'POST', headers: tenantHeader(tenantId) },
    ),

  // HU #10868 (Feature #10864, CF-01) — crea una prevalidación de identidad standalone (sin trámite).
  // POST /api/v1/tramites/biometric-validations. El backend encuentra o crea la entidad persona en el
  // tenant por (documentType, documentNumber), luego inicia la validación con el proveedor activo.
  // Contrato-first: el endpoint aún puede no estar mergeado en develop; el cliente está listo para
  // consumirlo en cuanto el backend (HU #10866) lo exponga.
  // 201 = creada; 202 = encolada (fallo transitorio del proveedor); 409 = ya existe prevalidación activa.
  createPrevalidacion: (
    body: IniciarPrevalidacionRequest,
    tenantId?: string,
  ): Promise<IniciarPrevalidacionResult> =>
    request<IniciarPrevalidacionResult>(
      '/api/v1/tramites/biometric-validations',
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
        body: JSON.stringify(body),
      },
    ),

  // HU #10944 (Feature #10864, CF-03, HU backend hermana #10943) — PATCH editar nombre/correo
  // (titular) y nombre/correo del RL de una prevalidación standalone. El documento NUNCA se envía
  // desde aquí (D7, no editable). Un cambio de correo dispara el reenvío automático en la misma
  // transacción (D8) — la respuesta trae `resent` y, si aplica, el nuevo `captureUrl`.
  // 403 no_editable · 404 not_found · 409 identidad_aprobada/referenciada_por_tramite ·
  // 422 documento_no_editable · 429 reenvio_en_cooldown/tope_reenvios · 502/503 proveedor.
  editPrevalidacion: (
    id: string,
    body: EditarPrevalidacionRequest,
    tenantId?: string,
  ): Promise<EditarPrevalidacionResult> =>
    request<EditarPrevalidacionResult>(
      `/api/v1/tramites/biometric-validations/${id}`,
      {
        method: 'PATCH',
        headers: tenantHeader(tenantId),
        body: JSON.stringify(body),
      },
    ),

  // HU #10944 (CF-03, D8) — POST reenvío manual sobre el MISMO registro: nuevo enlace, TTL 24h,
  // intentos/sondeos reiniciados en 0. 200 = envío completado; 202 = encolada (falla transitoria
  // del proveedor, ya consumió cupo del tope D10). Mismos guards/errores que editPrevalidacion.
  resendPrevalidacion: (
    id: string,
    tenantId?: string,
  ): Promise<ReenviarPrevalidacionResult> =>
    request<ReenviarPrevalidacionResult>(
      `/api/v1/tramites/biometric-validations/${id}/resend`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
      },
    ),

  // HU #10875 (AC1/AC2) — alertas/recordatorios de validación de identidad de UN trámite: mismo
  // clasificador del backend (HU #10873) acotado a las partes de esta instancia. Entrega POR PULL (sin
  // campana ni push); alimenta el panel consolidado de identidad del detalle del trámite.
  getInstanceIdentityValidationAlerts: async (
    instanceId: string,
    tenantId?: string,
  ): Promise<IdentityValidationAlertsResponse> => {
    const res = await request<IdentityValidationAlertsResponse>(
      `/api/v1/tramites/instances/${instanceId}/identity-validation/alerts`,
      { headers: tenantHeader(tenantId) },
    );
    return res ?? { alerts: [], total: 0 };
  },

  // GET estado biométrico completo (validaciones + proveedor configurado). El `provider` permite que
  // el botón "Validar identidad" sea provider-aware (kyverum → validación real; mock → simular).
  getBiometricState: async (
    instanceId: string,
    tenantId?: string,
  ): Promise<BiometricValidationsResponse> => {
    const res = await request<BiometricValidationsResponse>(
      `/api/v1/tramites/instances/${instanceId}/biometric`,
      { headers: tenantHeader(tenantId) },
    );
    return res ?? { validations: [], provider: 'mock' };
  },

  // GET descargar el certificado (PDF) de una validación de identidad desde Kyverum (keyed por
  // validationId → sirve para comprador o vendedor). Mismo patrón blob que downloadAttachment. El
  // mensaje de error usa el ProblemDetails del backend (p.ej. "No hay certificado disponible…") para
  // que el consumidor lo muestre tal cual. 404 sin_certificado/not_found; 502/503 proveedor.
  downloadBiometricCertificado: async (
    instanceId: string,
    validationId: string,
    tenantId?: string,
  ): Promise<{ blob: Blob; filename: string; mimetype: string }> => {
    const res = await fetch(
      apiUrl(
        `/api/v1/tramites/instances/${instanceId}/biometric/${validationId}/certificado`,
      ),
      { headers: tenantHeader(tenantId) },
    );
    if (!res.ok) {
      const body = await res.text().catch(() => '');
      throw new Error(problemMessage(res, body));
    }
    const blob = await res.blob();
    const mimetype = res.headers.get('content-type') ?? 'application/pdf';
    const cd = res.headers.get('content-disposition') ?? '';
    const star = /filename\*=(?:UTF-8'')?([^;]+)/i.exec(cd);
    const plain = /filename="?([^";]+)"?/i.exec(cd);
    const raw = star?.[1] ?? plain?.[1] ?? '';
    let filename = raw.trim();
    try {
      filename = raw ? decodeURIComponent(raw.trim()) : '';
    } catch {
      // raw no era URI-encoded; se usa tal cual.
    }
    return {
      blob,
      filename: filename || `certificado_identidad_${validationId}.pdf`,
      mimetype,
    };
  },

  // POST reconciliar una validación con el proveedor (fallback si el webhook no llegó): consulta el
  // estado real en Kyverum y lo aplica si ya es terminal. Idempotente. Devuelve { status, updated }.
  // El wizard lo usa para desatascar en vivo una validación colgada en `en_proceso`.
  reconcileBiometric: (
    instanceId: string,
    validationId: string,
    tenantId?: string,
  ): Promise<ReconcileIdentityResult> =>
    request<ReconcileIdentityResult>(
      `/api/v1/tramites/instances/${instanceId}/biometric/${validationId}/reconcile`,
      { method: 'POST', headers: tenantHeader(tenantId) },
    ),

  // GET bitácora (solo lectura) del ciclo de una validación: envío, llegada del webhook, si descifró el
  // secreto, firma, resultado y reconciliaciones. Sin PII/secretos. Diagnóstico de soporte desde la UI.
  getBiometricAudit: (
    instanceId: string,
    validationId: string,
    tenantId?: string,
  ): Promise<IdentityAuditResponse> =>
    request<IdentityAuditResponse>(
      `/api/v1/tramites/instances/${instanceId}/biometric/${validationId}/audit`,
      { headers: tenantHeader(tenantId) },
    ),

  // GET la misma bitácora, SIN depender de instancia (CF-07, Feature #11004, HU #11007): sirve tanto
  // a prevalidaciones standalone como a validaciones de trámite. Visible para cualquier rol del módulo
  // (D2 — no restringido a SuperAdmin); el saneo lo sigue haciendo el backend.
  getBiometricAuditByValidation: (
    validationId: string,
    tenantId?: string,
  ): Promise<IdentityAuditResponse> =>
    request<IdentityAuditResponse>(
      `/api/v1/tramites/biometric-validations/${validationId}/audit`,
      { headers: tenantHeader(tenantId) },
    ),

  // GET detalle de UNA validación por id (CF-06, Feature #11004, HU #11008): tenant-scoped, sirve
  // tanto a prevalidaciones standalone como a validaciones de trámite. Mismo DTO que el estado por-
  // instancia (BiometricValidationDto); pensado para poll (patrón KyverumPendingView, 5s).
  getPrevalidacionDetail: (id: string, tenantId?: string): Promise<BiometricValidation> =>
    request<BiometricValidation>(`/api/v1/tramites/biometric-validations/${id}`, {
      headers: tenantHeader(tenantId),
    }),

  // ── Firma electrónica (Slice 7A) — lado gestor autenticado ──────────
  // POST solicitar firma de una parte de la compraventa. Solo traspaso
  // (matrícula → 409 no_aplica). Idempotente por (parte, docTipo).
  solicitarFirma: (
    instanceId: string,
    input: SolicitarFirmaInput,
    tenantId?: string,
  ) =>
    request<Signature>(
      `/api/v1/tramites/instances/${instanceId}/signatures`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
        body: JSON.stringify(input),
      },
    ),

  // GET lista/estado de firmas. Desempaqueta a arreglo.
  listFirmas: async (
    instanceId: string,
    tenantId?: string,
  ): Promise<Signature[]> => {
    const res = await request<SignaturesResponse>(
      `/api/v1/tramites/instances/${instanceId}/signatures`,
      { headers: tenantHeader(tenantId) },
    );
    return res?.signatures ?? [];
  },

  // POST simular firma (mock complete) -> firmada.
  simularFirma: (
    instanceId: string,
    signatureId: string,
    tenantId?: string,
  ) =>
    request<SimularFirmaResult>(
      `/api/v1/tramites/instances/${instanceId}/signatures/${signatureId}/simulate`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
      },
    ),

  // ── FUR / compraventa (Slice 7A) ────────────────────────────────────
  // POST generar FUR (+ compraventa en traspaso). Gated por biométrica:
  // 409 biometria_gate si la requerida no está aprobada. Los documentos
  // generados se listan vía getAttachments (tipos fur/compraventa).
  generarFur: (instanceId: string, tenantId?: string) =>
    request<GenerarFurResult>(
      `/api/v1/tramites/instances/${instanceId}/fur`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
      },
    ),

  // GET formato de FUR que aplica según la clasificación del vehículo (HU #10924). Backend = fuente de
  // verdad; la UI solo lo muestra.
  getFurTemplateFormat: (instanceId: string, tenantId?: string) =>
    request<FurTemplateFormatResult>(
      `/api/v1/tramites/instances/${instanceId}/fur/template-format`,
      { headers: tenantHeader(tenantId) },
    ),

  // POST generar expediente consolidado (matrícula inicial). Fusiona FUR + adjuntos.
  // 409 fur_requerido | documentos_incompletos | modalidad_no_soportada.
  // Feature #11066 — `force=true` invalida caché y regenera desde cero (sin duplicar).
  generarConsolidado: (instanceId: string, tenantId?: string, force = false) =>
    request<GenerarConsolidadoResult>(
      `/api/v1/tramites/instances/${instanceId}/consolidado${force ? '?force=true' : ''}`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
      },
    ),

  // POST generar impronta (Kyverum RUNT) con los datos del trámite y adjuntarla. Idempotente por
  // NO-regeneración: 409 impronta_ya_existe si ya hay un adjunto tipo 'impronta' (manual o generado).
  // Otros errores: organismo_requerido | identificador_vehiculo_requerido |
  // documento_propietario_requerido | operador_no_resuelto | provider_validation |
  // provider_unauthorized | provider_unavailable.
  generarImpronta: (instanceId: string, tenantId?: string) =>
    request<GenerarImprontaAttachmentResult>(
      `/api/v1/tramites/instances/${instanceId}/attachments/generate-impronta`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
      },
    ),

  // PATCH diferir la impronta al paso FUR: marca el ítem de checklist como "se generará
  // automáticamente" (sin adjuntar) para poder continuar el paso 2 aunque sea obligatoria. `false`
  // revierte la marca. NO permite radicar sin la impronta real (SubmitGate la sigue exigiendo).
  setImprontaDiferida: (instanceId: string, diferida: boolean, tenantId?: string) =>
    request<void>(
      `/api/v1/tramites/instances/${instanceId}/checklist/impronta-diferida`,
      {
        method: 'PATCH',
        headers: tenantHeader(tenantId),
        body: JSON.stringify({ diferida }),
      },
    ),

  // ── Participantes del portal (Slice 7B) — lado gestor autenticado ───
  // POST invitar participante. Devuelve el token CRUDO + magicLinkPath
  // (/portal/{token}) solo aquí (en BD se persiste solo el hash).
  invitarParticipante: (
    instanceId: string,
    input: InvitarParticipanteInput,
    tenantId?: string,
  ) =>
    request<InvitarParticipanteResult>(
      `/api/v1/tramites/instances/${instanceId}/participants`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
        body: JSON.stringify(input),
      },
    ),

  // GET lista de participantes. Desempaqueta a arreglo.
  listParticipantes: async (
    instanceId: string,
    tenantId?: string,
  ): Promise<Participant[]> => {
    const res = await request<ParticipantsResponse>(
      `/api/v1/tramites/instances/${instanceId}/participants`,
      { headers: tenantHeader(tenantId) },
    );
    return res?.participants ?? [];
  },

  // POST reinvitar (rota token + reinicia expiración). 429 reminder_cooldown.
  reinvitarParticipante: (
    instanceId: string,
    participantId: string,
    tenantId?: string,
  ) =>
    request<InvitarParticipanteResult>(
      `/api/v1/tramites/instances/${instanceId}/participants/${participantId}/reinvite`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
      },
    ),

  // HU-2 (N03, RF05) — historial de transiciones de estado, paginado, más reciente primero.
  getStatusHistory: (
    instanceId: string,
    page = 1,
    pageSize = 20,
    tenantId?: string,
  ) =>
    request<StatusHistoryPage>(
      `/api/v1/tramites/instances/${instanceId}/status-history?page=${page}&pageSize=${pageSize}`,
      { headers: tenantHeader(tenantId) },
    ),

  // ── N 03 — transición de estado de negocio ──────────────────────
  // POST /instances/{id}/transition. Errores: ProblemDetails con title = CÓDIGO
  // (transicion_no_permitida, estado_final, identidad_no_aprobada, documentos_incompletos,
  // motivo_requerido, conflicto_concurrencia 409, estado_desconocido) y detail = mensaje;
  // aquí se mapea el código a copy UX (fallback: el detail del backend).
  transitionInstance: async (
    instanceId: string,
    toStatus: string,
    reason?: string,
    tenantId?: string,
  ): Promise<InstanceSummary> => {
    const token = getToken();
    const res = await fetch(
      apiUrl(`/api/v1/tramites/instances/${instanceId}/transition`),
      {
        method: 'POST',
        headers: {
          ...JSON_HEADERS,
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
          ...tenantHeader(tenantId),
        },
        body: JSON.stringify({ toStatus, reason: reason ?? null }),
      },
    );
    if (!res.ok) {
      const body = await res.text().catch(() => '');
      let code: string | undefined;
      let detail: string | undefined;
      try {
        const problem = JSON.parse(body) as { title?: string; detail?: string };
        code = problem.title;
        detail = problem.detail;
      } catch {
        // cuerpo no-JSON (gateway) → mensaje genérico abajo.
      }
      throw new Error(
        (code && TRANSITION_ERROR_COPY[code]) ?? detail ?? problemMessage(res, body),
      );
    }
    return (await res.json()) as InstanceSummary;
  },

  /** Activa subsanación sobre rechazado (flag; no cambia status). */
  startSubsanacion: (instanceId: string, tenantId?: string) =>
    request<InstanceSummary>(
      `/api/v1/tramites/instances/${instanceId}/subsanar`,
      { method: 'POST', headers: tenantHeader(tenantId) },
    ),

  /** Cancela subsanación (apaga el flag; el trámite sigue en rechazado). */
  cancelSubsanacion: (instanceId: string, tenantId?: string) =>
    request<InstanceSummary>(
      `/api/v1/tramites/instances/${instanceId}/cancelar-subsanacion`,
      { method: 'POST', headers: tenantHeader(tenantId) },
    ),
};

/** N 03 — copy UX por código de error del endpoint de transición (title del ProblemDetails). */
const TRANSITION_ERROR_COPY: Record<string, string> = {
  transicion_no_permitida: 'La transición de estado solicitada no está permitida.',
  estado_final: 'El trámite está en un estado final y no admite cambios.',
  identidad_no_aprobada: 'La validación de identidad del comprador no está aprobada.',
  documentos_incompletos: 'Faltan documentos obligatorios del trámite.',
  motivo_requerido: 'Debes indicar el motivo para esta transición.',
  conflicto_concurrencia: 'El trámite fue modificado por otro usuario, recarga e intenta de nuevo.',
  estado_desconocido: 'El estado destino no es válido.',
};

/**
 * Cliente PÚBLICO del portal de participantes (magic-link). Sin auth ni tenant
 * header: el token de alta entropía es la credencial. Usado por /portal/[token].
 * SEGURIDAD: token inválido/expirado/usado → 404 not_found genérico.
 */
export const portalPublicClient = {
  // GET vista del portal por token (consent + pasos pendientes).
  getByToken: (token: string) =>
    request<PortalView>(
      `/api/v1/public/portal/${encodeURIComponent(token)}`,
    ),

  // POST aceptar consentimiento Ley 1581 (IP/UA los captura el backend).
  aceptarConsentimiento: (token: string) =>
    request<AceptarConsentimientoResult>(
      `/api/v1/public/portal/${encodeURIComponent(token)}/consent`,
      { method: 'POST' },
    ),

  // POST subir documento (multipart: file + tipo). El browser fija el boundary.
  subirDocumento: async (
    token: string,
    tipo: string,
    file: File,
  ): Promise<ProcedureAttachment> => {
    const form = new FormData();
    form.append('file', file);
    form.append('tipo', tipo);
    const res = await fetch(
      apiUrl(`/api/v1/public/portal/${encodeURIComponent(token)}/documentos`),
      { method: 'POST', body: form },
    );
    if (!res.ok) {
      const body = await res.text().catch(() => '');
      throw new Error(
        `${res.status} ${res.statusText}${body ? ': ' + body : ''}`,
      );
    }
    return (await res.json()) as ProcedureAttachment;
  },

  // GET estado/URL de firma del participante.
  getFirma: (token: string) =>
    request<PortalFirmaUrl>(
      `/api/v1/public/portal/${encodeURIComponent(token)}/firma`,
    ),

  // POST simular firma (mock complete) desde el portal.
  simularFirma: (token: string) =>
    request<SimularFirmaResult>(
      `/api/v1/public/portal/${encodeURIComponent(token)}/firma/simulate`,
      { method: 'POST' },
    ),

  // POST finalizar (revoca el token: uso único).
  finalizar: (token: string) =>
    request<FinalizarPortalResult>(
      `/api/v1/public/portal/${encodeURIComponent(token)}/finalizar`,
      { method: 'POST' },
    ),
};

/**
 * Cliente PÚBLICO de la biométrica (magic-link). Sin auth ni tenant header:
 * el token de alta entropía es la credencial. Usado por la página /biometric/[token].
 */
export const biometricPublicClient = {
  // GET info de la tarea por token (vista pública sin PII sensible).
  getByToken: (token: string) =>
    request<BiometriaPublicView>(
      `/api/v1/public/biometric/${encodeURIComponent(token)}`,
    ),

  // POST completar con las 3 fotos (multipart). El browser fija el boundary;
  // NO se setea Content-Type. Nombres de campo EXACTOS del backend:
  // rostro | cedula_frontal | cedula_reverso.
  complete: async (
    token: string,
    photos: { rostro: File; cedulaFrontal: File; cedulaReverso: File },
  ): Promise<CompletarBiometriaResult> => {
    const form = new FormData();
    form.append('rostro', photos.rostro);
    form.append('cedula_frontal', photos.cedulaFrontal);
    form.append('cedula_reverso', photos.cedulaReverso);
    const res = await fetch(
      apiUrl(`/api/v1/public/biometric/${encodeURIComponent(token)}`),
      { method: 'POST', body: form },
    );
    if (!res.ok) {
      const body = await res.text().catch(() => '');
      throw new Error(
        `${res.status} ${res.statusText}${body ? ': ' + body : ''}`,
      );
    }
    return (await res.json()) as CompletarBiometriaResult;
  },
};
