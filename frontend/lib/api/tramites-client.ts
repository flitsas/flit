import type { ProcedureTypeSummary } from './types/procedure-parametrization';
import type {
  AceptarConsentimientoResult,
  ActorsResponse,
  AttachmentsResponse,
  BiometriaPublicView,
  BiometricParte,
  BiometricValidation,
  BiometricValidationsResponse,
  ChecklistView,
  CommercialData,
  CompletarBiometriaResult,
  ConsultationResult,
  CreateInstanceRequest,
  EnsureIdentityResult,
  FieldValueInput,
  FinalizarPortalResult,
  GenerarFurResult,
  GenerarConsolidadoResult,
  InstanceSummary,
  InstancesResponse,
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
  PreflightSnapshot,
  PresignAttachmentResponse,
  ProcedureActor,
  ProcedureAttachment,
  ProcedureConfiguration,
  ProcedureInstanceDetail,
  ProcedureInstanceSummary,
  RuntPersonLookupInput,
  RuntPersonLookupResult,
  Signature,
  SignaturesResponse,
  SimularFirmaResult,
  SolicitarFirmaInput,
  TenantBiometricValidationsResponse,
  TenantBiometricValidationFilters,
  StuckIdentityValidationsResponse,
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
  }>;
  provider?: string;
  createdAt: string;
}

function mapPreflight(dto: PreflightSnapshotDto): PreflightSnapshot {
  return {
    overall: dto.overall,
    checks: dto.checks.map((c) => ({
      key: c.key,
      label: c.label,
      status: c.status,
      source: c.source,
      message: c.message ?? '',
    })),
    createdAt: dto.createdAt,
  };
}
import { DEV_TENANT_ID, DEV_USER_ID } from './dev-constants';
import { getToken } from './client';
import { decodeJwtPayload } from '@/lib/auth/jwt';

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
 * ProblemDetails (RFC 7807) con `detail`/`title`, usa ese texto para mostrarlo tal
 * cual al usuario (p. ej. "La compañía no tiene habilitada la matrícula inicial.").
 * Si no es JSON, cae al formato genérico `status statusText: body`.
 */
function problemMessage(res: Response, body: string): string {
  if (body) {
    try {
      const problem = JSON.parse(body) as { detail?: string; title?: string };
      const msg = problem.detail || problem.title;
      if (msg) return msg;
    } catch {
      // body no es JSON → formato genérico abajo.
    }
  }
  return `${res.status} ${res.statusText}${body ? ': ' + body : ''}`;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
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
    throw new Error(problemMessage(res, body));
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
function tenantHeader(tenantId?: string): HeadersInit {
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
  // GET devuelve { items }; se desempaqueta al arreglo para el consumidor.
  // #1 — El tenant lo deriva el backend del JWT: company-user ve solo su compañía. El SuperAdmin
  // ve TODO; solo se manda X-Tenant-Id si elige una compañía (filterTenantId).
  listInstances: async (
    filterTenantId?: string,
  ): Promise<InstanceSummary[]> => {
    const headers: Record<string, string> = {};
    if (filterTenantId) headers['X-Tenant-Id'] = filterTenantId;
    const res = await request<InstancesResponse>(
      '/api/v1/tramites/instances',
      { headers },
    );
    // Normaliza los campos async de HU #10350 con defaults seguros: un backend que aún no los
    // exponga (transición) deja la tabla funcionando (chips/estado base) sin romper el render.
    return (res?.items ?? []).map((item) => ({
      ...item,
      draftFinalizedAt: item.draftFinalizedAt ?? null,
      identityValidationStatus: item.identityValidationStatus ?? null,
      signaturePending: item.signaturePending ?? false,
      canSubmit: item.canSubmit ?? false,
    }));
  },

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
  finalizeDraft: (id: string, tenantId: string = DEV_TENANT_ID) =>
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

  // #10201 — consulta real de fuentes externas (RUNT/SIMIT). Mapea
  // ConsultationResult del backend al shape PreflightSnapshot del panel.
  runConsultation: async (
    instanceId: string,
    templateCode: string,
    tenantId?: string,
  ): Promise<PreflightSnapshot> => {
    const result = await request<ConsultationResult>(
      `/api/v1/tramites/instances/${instanceId}/consultations/${templateCode}`,
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
    };
  },

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
    return { blob, filename: filename || attachmentId, mimetype };
  },

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
    tenantId: string = DEV_TENANT_ID,
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
    add('nombre', filters.nombre);
    add('parte', filters.parte);
    add('tipoDoc', filters.tipoDoc);
    add('documento', filters.documento);
    add('estado', filters.estado);
    add('provider', filters.provider);
    add('scoreMin', filters.scoreMin);
    add('scoreMax', filters.scoreMax);
    add('createdFrom', filters.createdFrom);
    add('createdTo', filters.createdTo);
    add('motivoRechazo', filters.motivoRechazo);
    add('vigenciaEstado', filters.vigenciaEstado);
    add('expiraDesde', filters.expiraDesde);
    add('expiraHasta', filters.expiraHasta);
    add('venceEnDias', filters.venceEnDias);
    add('page', filters.page);
    add('pageSize', filters.pageSize);

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

  // POST generar expediente consolidado (matrícula inicial). Fusiona FUR + adjuntos.
  // 409 fur_requerido | documentos_incompletos | modalidad_no_soportada.
  generarConsolidado: (instanceId: string, tenantId?: string) =>
    request<GenerarConsolidadoResult>(
      `/api/v1/tramites/instances/${instanceId}/consolidado`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
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
