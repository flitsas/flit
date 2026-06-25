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
  FieldValueInput,
  FinalizarPortalResult,
  GenerarFurResult,
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

export { DEV_TENANT_ID, DEV_USER_ID };

// La API vive en otro origen (api.<env>.flitsas.online); el CD inyecta
// NEXT_PUBLIC_API_BASE_URL (la MISMA variable que usa lib/api/client.ts). Compat con
// NEXT_PUBLIC_API_URL (entornos locales) y localhost para dev. ANTES leía solo
// NEXT_PUBLIC_API_URL → en DEV quedaba vacío → las llamadas caían al mismo origen
// (el frontend Next.js, que no sirve /api) y devolvían 500.
const BASE_URL =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:4002';

// Único constructor de URLs del cliente. El path absoluto (/api/v1/...) toma solo el
// ORIGEN de BASE_URL e ignora su path, así un BASE_URL con sufijo /api/v1 (el que inyecta
// el CD) NO se duplica (`…/api/v1/api/v1/…` → 404). Mismo patrón que lib/api/client.ts.
// Usarlo SIEMPRE; no concatenar `${BASE_URL}${path}` (rompe cuando el base trae sufijo).
export const apiUrl = (path: string): string => new URL(path, BASE_URL).toString();

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
  const res = await fetch(apiUrl(path), {
    ...init,
    headers: { ...JSON_HEADERS, ...init?.headers },
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

/** Header de tenant para runtime (NO X-Flit-SuperAdmin). */
function tenantHeader(tenantId: string = DEV_TENANT_ID): HeadersInit {
  return { 'X-Tenant-Id': tenantId };
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
  listInstances: async (
    tenantId: string = DEV_TENANT_ID,
  ): Promise<InstanceSummary[]> => {
    const res = await request<InstancesResponse>(
      '/api/v1/tramites/instances',
      { headers: tenantHeader(tenantId) },
    );
    return res?.items ?? [];
  },

  // #2 — Organismos de tránsito habilitados para la empresa (tenant del header).
  // El operador solo puede elegir/enviar a estos en el FUR.
  listTransitOffices: async (
    tenantId: string = DEV_TENANT_ID,
  ): Promise<TransitOfficeOption[]> => {
    const res = await request<TransitOfficesResponse>(
      '/api/v1/tramites/transit-offices',
      { headers: tenantHeader(tenantId) },
    );
    return res?.items ?? [];
  },

  getInstance: (id: string, tenantId: string = DEV_TENANT_ID) =>
    request<ProcedureInstanceDetail>(`/api/v1/tramites/instances/${id}`, {
      headers: tenantHeader(tenantId),
    }),

  // AC3 — guardar borrador (409 not_draft si ya enviada).
  patchFieldValues: (
    id: string,
    items: FieldValueInput[],
    tenantId: string = DEV_TENANT_ID,
  ) =>
    request<ProcedureInstanceDetail>(
      `/api/v1/tramites/instances/${id}/field-values`,
      {
        method: 'PATCH',
        headers: tenantHeader(tenantId),
        body: JSON.stringify({ items }),
      },
    ),

  submitInstance: (id: string, tenantId: string = DEV_TENANT_ID) =>
    request<ProcedureInstanceSummary>(
      `/api/v1/tramites/instances/${id}/submit`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
      },
    ),

  // Slice 2 — actores del trámite. GET devuelve { actors }; se desempaqueta
  // para que los consumidores reciban directamente el arreglo.
  getActors: async (
    instanceId: string,
    tenantId: string = DEV_TENANT_ID,
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
    tenantId: string = DEV_TENANT_ID,
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
    tenantId: string = DEV_TENANT_ID,
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
    tenantId: string = DEV_TENANT_ID,
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
  getChecklist: (instanceId: string, tenantId: string = DEV_TENANT_ID) =>
    request<ChecklistView>(
      `/api/v1/tramites/instances/${instanceId}/checklist`,
      { headers: tenantHeader(tenantId) },
    ),

  // GET adjuntos. Devuelve { attachments }; se desempaqueta al arreglo.
  getAttachments: async (
    instanceId: string,
    tenantId: string = DEV_TENANT_ID,
  ): Promise<ProcedureAttachment[]> => {
    const res = await request<AttachmentsResponse>(
      `/api/v1/tramites/instances/${instanceId}/attachments`,
      { headers: tenantHeader(tenantId) },
    );
    return res?.attachments ?? [];
  },

  // POST multipart. NO se fija Content-Type: el browser pone el boundary
  // del multipart/form-data automáticamente al pasar un FormData.
  uploadAttachment: async (
    instanceId: string,
    tipo: string,
    file: File,
    tenantId: string = DEV_TENANT_ID,
  ): Promise<ProcedureAttachment> => {
    const form = new FormData();
    form.append('file', file);
    form.append('tipo', tipo);
    const res = await fetch(
      apiUrl(`/api/v1/tramites/instances/${instanceId}/attachments`),
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
        body: form,
      },
    );
    if (!res.ok) {
      const body = await res.text().catch(() => '');
      throw new Error(
        `${res.status} ${res.statusText}${body ? ': ' + body : ''}`,
      );
    }
    return (await res.json()) as ProcedureAttachment;
  },

  // GET descarga binaria de un adjunto. Devuelve el blob + filename/mimetype
  // (resueltos del Content-Disposition / Content-Type de la respuesta) para que
  // el consumidor dispare la descarga del navegador (blob → objectURL → anchor).
  downloadAttachment: async (
    instanceId: string,
    attachmentId: string,
    tenantId: string = DEV_TENANT_ID,
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
    tenantId: string = DEV_TENANT_ID,
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
  getWizardState: (instanceId: string, tenantId: string = DEV_TENANT_ID) =>
    request<WizardState>(
      `/api/v1/tramites/instances/${instanceId}/wizard`,
      { headers: tenantHeader(tenantId) },
    ),

  // ── Preflight (semáforo legal) — Slice 4b/5 ─────────────────────
  // POST corre la consulta; GET trae el último snapshot. Ambos mapean
  // al shape PreflightSnapshot que consume el PreflightPanel.
  runPreflight: async (
    instanceId: string,
    tenantId: string = DEV_TENANT_ID,
  ): Promise<PreflightSnapshot> => {
    const dto = await request<PreflightSnapshotDto>(
      `/api/v1/tramites/instances/${instanceId}/preflight`,
      { method: 'POST', headers: tenantHeader(tenantId) },
    );
    return mapPreflight(dto);
  },

  getPreflight: async (
    instanceId: string,
    tenantId: string = DEV_TENANT_ID,
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
  getCommercial: (instanceId: string, tenantId: string = DEV_TENANT_ID) =>
    request<CommercialData>(
      `/api/v1/tramites/instances/${instanceId}/commercial`,
      { headers: tenantHeader(tenantId) },
    ),

  putCommercial: (
    instanceId: string,
    data: CommercialData,
    tenantId: string = DEV_TENANT_ID,
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
    tenantId: string = DEV_TENANT_ID,
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
    tenantId: string = DEV_TENANT_ID,
  ) =>
    request<BiometricValidation>(
      `/api/v1/tramites/instances/${instanceId}/biometric/simulate`,
      {
        method: 'POST',
        headers: tenantHeader(tenantId),
        body: JSON.stringify(input),
      },
    ),

  // GET lista/estado de las validaciones de la instancia. Desempaqueta a arreglo.
  listBiometric: async (
    instanceId: string,
    tenantId: string = DEV_TENANT_ID,
  ): Promise<BiometricValidation[]> => {
    const res = await request<BiometricValidationsResponse>(
      `/api/v1/tramites/instances/${instanceId}/biometric`,
      { headers: tenantHeader(tenantId) },
    );
    return res?.validations ?? [];
  },

  // GET estado biométrico completo (validaciones + proveedor configurado). El `provider` permite que
  // el botón "Validar identidad" sea provider-aware (kyverum → validación real; mock → simular).
  getBiometricState: async (
    instanceId: string,
    tenantId: string = DEV_TENANT_ID,
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
    tenantId: string = DEV_TENANT_ID,
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
    tenantId: string = DEV_TENANT_ID,
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
    tenantId: string = DEV_TENANT_ID,
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
  generarFur: (instanceId: string, tenantId: string = DEV_TENANT_ID) =>
    request<GenerarFurResult>(
      `/api/v1/tramites/instances/${instanceId}/fur`,
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
    tenantId: string = DEV_TENANT_ID,
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
    tenantId: string = DEV_TENANT_ID,
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
    tenantId: string = DEV_TENANT_ID,
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
