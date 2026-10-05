// Cliente de la revisión manual de identidad (Épica #13202, Feature C). Solo Super Admin.
// Contrato: docs/design/EPICA-13202-contrato-api.md §3. Por defecto usa el adaptador SIMULADO
// (datos sintéticos); `NEXT_PUBLIC_MANUAL_REVIEW_MOCK=false` conmuta al cliente HTTP real sin tocar
// los componentes.
import { apiFetch, friendlyErrorMessage, getToken, resolveApiUrl } from './client';
import { ApiError } from './types';
import { createMockManualReviewClient } from './manual-review-mock';
import type {
  ManualDetail,
  ManualImageKind,
  ManualListParams,
  ManualListResponse,
} from './types/manual-review';

export interface ManualReviewClient {
  listManual(params: ManualListParams, signal?: AbortSignal): Promise<ManualListResponse>;
  getManualDetail(id: string, signal?: AbortSignal): Promise<ManualDetail>;
  /** Imagen protegida como Blob: el JWT viaja en la cabecera, nunca en la URL. */
  getManualImage(id: string, kind: ManualImageKind, signal?: AbortSignal): Promise<Blob>;
  approveManual(id: string): Promise<void>;
  rejectManual(id: string, reasonCode: string): Promise<void>;
}

const BASE = '/api/v1/tramites/biometric-validations';

export const manualReviewHttpClient: ManualReviewClient = {
  async listManual(params, signal) {
    const res = await apiFetch<ManualListResponse>(`${BASE}/manual`, {
      query: {
        page: params.page,
        pageSize: params.pageSize,
        status: params.status || undefined,
        origin: params.origin || undefined,
        q: params.q?.trim() || undefined,
      },
      signal,
    });
    return res ?? { items: [], total: 0, page: params.page, pageSize: params.pageSize };
  },

  getManualDetail: (id, signal) =>
    apiFetch<ManualDetail>(`${BASE}/${encodeURIComponent(id)}/manual-detail`, { signal }),

  async getManualImage(id, kind, signal) {
    const token = getToken();
    const response = await fetch(
      resolveApiUrl(`${BASE}/${encodeURIComponent(id)}/manual-images/${kind}`),
      { headers: token ? { Authorization: `Bearer ${token}` } : {}, signal },
    );
    if (!response.ok) {
      let body: { error?: unknown; detail?: unknown; title?: unknown } | null = null;
      try {
        body = (await response.json()) as typeof body;
      } catch {
        body = null;
      }
      throw new ApiError(response.status, friendlyErrorMessage(body), body);
    }
    return response.blob();
  },

  async approveManual(id) {
    await apiFetch<void>(`${BASE}/${encodeURIComponent(id)}/manual-approve`, { method: 'POST' });
  },

  async rejectManual(id, reasonCode) {
    await apiFetch<void>(`${BASE}/${encodeURIComponent(id)}/manual-reject`, {
      method: 'POST',
      body: { reasonCode },
    });
  },
};

/** `true` salvo que `NEXT_PUBLIC_MANUAL_REVIEW_MOCK` sea exactamente «false». */
export function manualReviewMockActivo(): boolean {
  return process.env.NEXT_PUBLIC_MANUAL_REVIEW_MOCK !== 'false';
}

let mockSingleton: ManualReviewClient | null = null;

/** Cliente que usa la UI: simulado por defecto, HTTP real con la variable en «false». */
export function getManualReviewClient(): ManualReviewClient {
  if (!manualReviewMockActivo()) return manualReviewHttpClient;
  mockSingleton ??= createMockManualReviewClient();
  return mockSingleton;
}
