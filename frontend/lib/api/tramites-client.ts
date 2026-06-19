import type { ProcedureTypeSummary } from './types/procedure-parametrization';
import type {
  ActorsResponse,
  AttachmentsResponse,
  ChecklistView,
  CommercialData,
  ConsultationResult,
  CreateInstanceRequest,
  FieldValueInput,
  PreflightSnapshot,
  ProcedureActor,
  ProcedureAttachment,
  ProcedureConfiguration,
  ProcedureInstanceDetail,
  ProcedureInstanceSummary,
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

/** Same-origin relative paths; Next.js rewrites proxy to core-api in dev. */
const BASE_URL = process.env.NEXT_PUBLIC_API_URL ?? '';

const JSON_HEADERS: HeadersInit = {
  'Content-Type': 'application/json',
};

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    ...init,
    headers: { ...JSON_HEADERS, ...init?.headers },
  });
  if (!res.ok) {
    const body = await res.text().catch(() => '');
    throw new Error(`${res.status} ${res.statusText}${body ? ': ' + body : ''}`);
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
  saveActors: (
    instanceId: string,
    actors: ProcedureActor[],
    tenantId: string = DEV_TENANT_ID,
  ) =>
    request<void>(`/api/v1/tramites/instances/${instanceId}/actors`, {
      method: 'PUT',
      headers: tenantHeader(tenantId),
      body: JSON.stringify({ actors }),
    }),

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
      `${BASE_URL}/api/v1/tramites/instances/${instanceId}/attachments`,
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
};
