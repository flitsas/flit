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
   * Vencimiento del enlace de captura vigente (24 h): solo con `manual_activo` (el cliente aún no capturó) o `rechazado`
   * (enlace NUEVO para repetir la captura, HU #13299); `null` en los demás. Espejo de `ManualDetail.linkExpiresAt`.
   */
  linkExpiresAt?: string | null;
}

/** Respuesta de `POST .../manual-approve` (espejo de `AprobarValidacionManualResult`). */
export interface ManualApproveResult {
  validationId: string;
  tenantId: string;
  procedureInstanceId: string | null;
  status: 'aprobado';
  approvalOrigin: 'manual';
  validatedAt: string;
  /** Fin de vigencia (30 días desde la aprobación). */
  validUntil: string;
  reviewedAt: string;
}

/** Respuesta de `POST .../manual-reject` (espejo de `RechazarValidacionManualResult`). */
export interface ManualRejectResult {
  validationId: string;
  tenantId: string;
  procedureInstanceId: string | null;
  status: 'rechazado';
  rejectionReasonCode: string;
  reviewedAt: string;
  /** Vencimiento del enlace NUEVO (24 h). */
  linkExpiresAt: string;
  /** `false`: el correo con el motivo y el enlace no salió (el rechazo sí quedó). */
  emailEnviado: boolean;
}
