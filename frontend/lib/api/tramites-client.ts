import type { ProcedureTypeSummary } from './types/procedure-parametrization';
import type {
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
};

// STUB #10201 — consulta de fuentes externas (RUNT/SIMIT) simulada en cliente.
// Sin HTTP real: devuelve un snapshot determinista para validar el panel semáforo.
// La consulta real se cablea en #10201.
export function runConsultationStub(): Promise<PreflightSnapshot> {
  const snapshot: PreflightSnapshot = {
    overall: 'yellow',
    createdAt: new Date().toISOString(),
    checks: [
      {
        key: 'runt_inscripcion',
        label: 'Inscripción RUNT',
        status: 'ok',
        source: 'RUNT',
        message: 'Vehículo inscrito y activo en el RUNT.',
      },
      {
        key: 'simit_comparendos',
        label: 'Comparendos',
        status: 'warn',
        source: 'SIMIT',
        message: '1 comparendo pendiente por $234.500 COP.',
        action: { label: 'Ver detalle en SIMIT', ctaId: 'simit_detail' },
      },
      {
        key: 'runt_soat',
        label: 'SOAT vigente',
        status: 'fail',
        source: 'RUNT',
        message: 'SOAT vencido. Renueva antes de radicar.',
        action: { label: 'Renovar SOAT', ctaId: 'soat_renew' },
      },
    ],
  };
  return Promise.resolve(snapshot);
}
