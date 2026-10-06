// Tipos de la revisión manual de identidad (Épica #13202, Feature C).
// Fuente: docs/design/EPICA-13202-contrato-api.md §3 (ManualListItem / ManualDetail).

export type ManualOrigin = 'tramite' | 'prevalidacion' | 'mandatario' | 'representante_legal';

export type ManualStatus =
  | 'manual_activo'
  | 'pendiente_revision_manual'
  | 'aprobado'
  | 'rechazado'
  | 'expirado';

export type ManualImageKind = 'rostro' | 'anverso' | 'reverso' | 'firma';

export const MANUAL_IMAGE_KINDS: readonly ManualImageKind[] = ['rostro', 'anverso', 'reverso', 'firma'];

export interface ManualListItem {
  id: string;
  fullName: string;
  documentNumber: string;
  tenantName: string;
  origin: ManualOrigin;
  status: ManualStatus;
  /** ISO 8601 (instante). */
  activatedAt: string | null;
  /**
   * Minutos que lleva esperando REVISIÓN (desde que el cliente envió la captura). `null` salvo en
   * `pendiente_revision_manual`: la UI muestra «—».
   */
  waitingMinutes: number | null;
}

export interface ManualListParams {
  page: number;
  pageSize: number;
  status?: ManualStatus | '';
  origin?: ManualOrigin | '';
  q?: string;
}

export interface ManualListResponse {
  items: ManualListItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ManualDetail extends ManualListItem {
  consentAt: string | null;
  consentTextVersion: string | null;
  images: { kind: ManualImageKind; available: boolean }[];
  reviewedAt: string | null;
  reviewedBy: string | null;
  rejectionReasonCode: string | null;
  /**
   * NO está en el contrato §3: caducidad del enlace de captura (24 h), para el estado «el cliente aún no
   * ha capturado» (HU-C6 AC4). Opcional: si el backend no lo manda, la UI omite la línea.
   */
  linkExpiresAt?: string | null;
}
