import type { ProcedureTypeSummary } from './types/procedure-parametrization';
import type {
  ConsultationResult,
  CreateInstanceRequest,
  FieldValueInput,
  PreflightSnapshot,
  ProcedureConfiguration,
  ProcedureInstanceDetail,
  ProcedureInstanceSummary,
} from './types/procedure-runtime';
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
};
