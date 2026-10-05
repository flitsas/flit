// Adaptador SIMULADO de la revisión manual (Épica #13202). Datos 100 % sintéticos: sin personas
// reales; las «imágenes» son SVG generados en código. Mantiene estado en memoria para que aprobar o
// rechazar se refleje al volver a consultar.
import { ApiError } from './types';
import type { ManualReviewClient } from './manual-review-client';
import {
  MANUAL_IMAGE_KINDS,
  type ManualDetail,
  type ManualImageKind,
  type ManualListItem,
  type ManualOrigin,
  type ManualStatus,
} from './types/manual-review';
import { MOTIVOS_RECHAZO_MANUAL } from '@/lib/identidad/motivos-rechazo-manual';

const ORIGENES: ManualOrigin[] = ['tramite', 'prevalidacion', 'mandatario', 'representante_legal'];
const ESTADOS: ManualStatus[] = [
  'pendiente_revision_manual',
  'manual_activo',
  'pendiente_revision_manual',
  'aprobado',
  'rechazado',
  'expirado',
];
const COMPANIAS = ['Compañía Demo Uno', 'Compañía Demo Dos', 'Compañía Demo Tres'];

export interface MockOptions {
  /** Cantidad de filas sintéticas. Default 27 (3 páginas de 10). */
  count?: number;
  /** Latencia simulada en ms. Default 120. */
  delayMs?: number;
  /** Instante «ahora» fijo (tests). */
  now?: Date;
}

function svg(body: string): string {
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 320 220" width="320" height="220">${body}</svg>`;
}

/** Imagen sintética por tipo de captura: siluetas genéricas, sin rasgos de nadie. */
export function syntheticImageSvg(kind: ManualImageKind): string {
  switch (kind) {
    case 'rostro':
      return svg(
        '<rect width="320" height="220" fill="#EEF5FF"/><ellipse cx="160" cy="105" rx="52" ry="68" fill="#DFE5ED" stroke="#557EFF" stroke-width="3"/><circle cx="140" cy="95" r="5" fill="#162744"/><circle cx="180" cy="95" r="5" fill="#162744"/><path d="M142 130 Q160 145 178 130" stroke="#162744" stroke-width="3" fill="none"/>',
      );
    case 'anverso':
      return svg(
        '<rect width="320" height="220" fill="#EEF5FF"/><rect x="30" y="40" width="260" height="140" rx="12" fill="#fff" stroke="#557EFF" stroke-width="3"/><rect x="46" y="62" width="64" height="80" rx="6" fill="#DFE5ED"/><rect x="128" y="66" width="140" height="10" rx="4" fill="#DFE5ED"/><rect x="128" y="90" width="110" height="10" rx="4" fill="#DFE5ED"/><rect x="128" y="114" width="126" height="10" rx="4" fill="#DFE5ED"/>',
      );
    case 'reverso':
      return svg(
        '<rect width="320" height="220" fill="#EEF5FF"/><rect x="30" y="40" width="260" height="140" rx="12" fill="#fff" stroke="#557EFF" stroke-width="3"/><rect x="46" y="62" width="228" height="22" rx="4" fill="#DFE5ED"/><rect x="46" y="100" width="150" height="10" rx="4" fill="#DFE5ED"/><rect x="46" y="124" width="180" height="10" rx="4" fill="#DFE5ED"/><rect x="226" y="120" width="48" height="40" rx="4" fill="#DFE5ED"/>',
      );
    case 'firma':
    default:
      return svg(
        '<rect width="320" height="220" fill="#fff"/><path d="M40 140 C70 60 90 180 120 110 S170 70 190 130 S250 150 280 90" stroke="#162744" stroke-width="4" fill="none" stroke-linecap="round"/><line x1="30" y1="170" x2="290" y2="170" stroke="#DFE5ED" stroke-width="2"/>',
      );
  }
}

function buildRows(count: number, now: Date): ManualDetail[] {
  return Array.from({ length: count }, (_, i) => {
    const status = ESTADOS[i % ESTADOS.length];
    const waitingMinutes = 20 + i * 47;
    const activatedAt = new Date(now.getTime() - waitingMinutes * 60_000).toISOString();
    const captured = status !== 'manual_activo' && status !== 'expirado';
    const n = String(i + 1).padStart(2, '0');
    return {
      id: `mock-manual-${n}`,
      fullName: `Persona de prueba ${n}`,
      documentNumber: String(1000000000 + i * 7919),
      tenantName: COMPANIAS[i % COMPANIAS.length],
      origin: ORIGENES[i % ORIGENES.length],
      status,
      activatedAt,
      waitingMinutes,
      consentAt: captured ? new Date(now.getTime() - (waitingMinutes - 8) * 60_000).toISOString() : null,
      consentTextVersion: captured ? 'consentimiento-manual-v1' : null,
      images: MANUAL_IMAGE_KINDS.map((kind) => ({ kind, available: captured })),
      reviewedAt: status === 'aprobado' || status === 'rechazado' ? now.toISOString() : null,
      reviewedBy: null,
      rejectionReasonCode: status === 'rechazado' ? MOTIVOS_RECHAZO_MANUAL[i % 6].code : null,
      linkExpiresAt:
        status === 'manual_activo' ? new Date(now.getTime() + 20 * 3_600_000).toISOString() : null,
    };
  });
}

function toListItem(r: ManualDetail): ManualListItem {
  return {
    id: r.id,
    fullName: r.fullName,
    documentNumber: r.documentNumber,
    tenantName: r.tenantName,
    origin: r.origin,
    status: r.status,
    activatedAt: r.activatedAt,
    waitingMinutes: r.waitingMinutes,
  };
}

export function createMockManualReviewClient(options: MockOptions = {}): ManualReviewClient {
  const { count = 27, delayMs = 120, now = new Date() } = options;
  const rows = buildRows(count, now);
  const pause = () => (delayMs > 0 ? new Promise<void>((r) => setTimeout(r, delayMs)) : Promise.resolve());
  const find = (id: string): ManualDetail => {
    const row = rows.find((r) => r.id === id);
    if (!row) throw new ApiError(404, 'No se encontró la validación.');
    return row;
  };
  const exigirPendiente = (row: ManualDetail) => {
    if (row.status !== 'pendiente_revision_manual') {
      throw new ApiError(409, 'La validación ya no está pendiente de revisión.', { code: 'estado_invalido' });
    }
  };

  return {
    async listManual(params) {
      await pause();
      const q = params.q?.trim().toLowerCase() ?? '';
      const filtered = rows.filter(
        (r) =>
          (!params.status || r.status === params.status) &&
          (!params.origin || r.origin === params.origin) &&
          (!q || r.fullName.toLowerCase().includes(q) || r.documentNumber.includes(q)),
      );
      const start = (params.page - 1) * params.pageSize;
      return {
        items: filtered.slice(start, start + params.pageSize).map(toListItem),
        total: filtered.length,
        page: params.page,
        pageSize: params.pageSize,
      };
    },
    async getManualDetail(id) {
      await pause();
      return structuredClone(find(id));
    },
    async getManualImage(id, kind) {
      await pause();
      const row = find(id);
      if (!row.images.find((i) => i.kind === kind)?.available) {
        throw new ApiError(404, 'La imagen no está disponible.');
      }
      return new Blob([syntheticImageSvg(kind)], { type: 'image/svg+xml' });
    },
    async approveManual(id) {
      await pause();
      const row = find(id);
      exigirPendiente(row);
      row.status = 'aprobado';
      row.reviewedAt = new Date().toISOString();
    },
    async rejectManual(id, reasonCode) {
      await pause();
      if (!MOTIVOS_RECHAZO_MANUAL.some((m) => m.code === reasonCode)) {
        throw new ApiError(400, 'Motivo no válido.', { code: 'motivo_invalido' });
      }
      const row = find(id);
      exigirPendiente(row);
      row.status = 'rechazado';
      row.rejectionReasonCode = reasonCode;
      row.reviewedAt = new Date().toISOString();
    },
  };
}
