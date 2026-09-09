/**
 * Tipos del módulo "Generación documental" (HU-01, Feature #12201).
 *
 * Contrato: `.claude/state/diseno-feature-12201.md` §7.1/§7.2 —
 * base `/api/v1/admin/generacion-documental`, permisos `generacion-documental.read` /
 * `.generate`. `POST .../generate` responde SIEMPRE `application/json` con `{ id, status }`
 * y NUNCA el binario: la descarga va por `GET /{id}/download` (presigned URL).
 *
 * `document_snapshot` (PII alta) no se modela aquí a propósito: no se expone en listados y
 * el detalle de HU-01 no lo consume.
 */
import type { StandaloneDocumentStatus } from "@/components/admin/generacion-documental/status-labels";

export type StandaloneDocumentType = "certificado_rues" | "transferencia_dominio_generada";

/** Escenario normativo A/B/C — solo en transferencia (I2); `null` en Certificado RUES. */
export type StandaloneDocumentScenario = "A" | "B" | "C";

/** Fila del historial (CF-17): metadata mínima, sin snapshot. */
export interface StandaloneDocumentListItem {
  id: string;
  documentType: StandaloneDocumentType;
  scenario: StandaloneDocumentScenario | null;
  status: StandaloneDocumentStatus;
  errorCode?: string | null;
  filename?: string | null;
  companyName?: string | null;
  /** Autor de la generación. Alimenta el filtro por usuario del historial (CF-18). */
  createdByUserId: string;
  createdByUserName?: string | null;
  createdAt: string;
}

export interface StandaloneDocumentsPagedResult {
  items: StandaloneDocumentListItem[];
  page: number;
  pageSize: number;
  total: number;
}

/**
 * Filtros del historial. `status` viaja como los estados INTERNOS que cubre la opción de
 * usuario (`standaloneDocumentStatusQuery`), porque «En proceso» son dos: pending y
 * processing (CF-21).
 */
export interface StandaloneDocumentsListParams {
  documentType?: StandaloneDocumentType;
  status?: StandaloneDocumentStatus[];
  dateFrom?: string;
  dateTo?: string;
  userId?: string;
  /** Solo SuperAdmin: metadata global de otro tenant (CF-20). Nunca devuelve contenido. */
  tenantId?: string;
  page?: number;
  pageSize?: number;
}

/** Respuesta de `POST /rues/generate` y `POST /transferencia/generate`. */
export interface StandaloneDocumentGenerateResult {
  id: string;
  status: StandaloneDocumentStatus;
}

/** Respuesta de `GET /{id}/download` (CF-19). La URL no se loguea nunca. */
export interface StandaloneDocumentDownloadLink {
  url: string;
  expiresAt: string;
}

/** Campo devuelto por la revisión previa de RUES (`POST /rues/preview`). */
export interface StandaloneRuesPreviewField {
  key: string;
  label: string;
  value: string | null;
}

export interface StandaloneRuesPreviewResult {
  found: boolean;
  nit: string;
  fields: StandaloneRuesPreviewField[];
}

// ── Transferencia de dominio (HU #12207, Feature #12201) ────────────────────────────────────
//
// Contrato normativo: `docs/plantilla-transferencia-dominio.md`. Este incremento cubre el
// ESCENARIO A (traspaso ordinario, art. 5.3.2.1); los escenarios B y C, el control de régimen
// aplicable (VB-07) y los lotes llegan después.

/** Las 13 variables de vehículo del anexo §5.1. Todas son texto: el documento transcribe. */
export interface TransferVehiculoInput {
  placa: string;
  marca?: string;
  linea?: string;
  modeloAnio?: string;
  claseVehiculo?: string;
  tipoCarroceria?: string;
  color?: string;
  noMotor?: string;
  noChasis?: string;
  noSerie?: string;
  servicio?: string;
  noLicenciaTransito?: string;
  organismoTransito?: string;
}

/**
 * Una parte compareciente (anexo §5.2 y §5.3). `digitoVerificacion` no se envía: el backend lo
 * calcula (módulo 11 DIAN) para que un NIT y su DV no puedan discrepar dentro del mismo PDF.
 */
export interface TransferParteInput {
  tipoPersona?: "PN" | "PJ";
  nombreRazonSocial?: string;
  tipoDoc?: string;
  numeroDoc?: string;
  domicilio?: string;
  representanteLegal?: string;
  ccRepresentanteLegal?: string;
}

export type TransferTituloJuridico =
  | "COMPRAVENTA"
  | "DACION_EN_PAGO"
  | "PERMUTA"
  | "DONACION"
  | "OTRO";

/** Variables del negocio (§5.4), incluidas las tres fiscales que imprime la cláusula SEXTA. */
export interface TransferNegocioInput {
  tituloJuridico?: TransferTituloJuridico | "";
  descripcionTitulo?: string;
  precioLetras?: string;
  precioNumeros?: string;
  contraprestacionDescripcion?: string;
  formaPago?: string;
  asumeRetencionFuente?: "TRANSFERENTE" | "ADQUIRENTE" | "SEGUN_LEY";
  asumeDerechosTramite?: "TRANSFERENTE" | "ADQUIRENTE" | "COMPARTIDOS";
  asumeImpuestoVehiculo?: "TRANSFERENTE" | "ADQUIRENTE" | "SEGUN_LEY" | null;
  ciudadFirma?: string;
  fechaFirma?: string;
}

/** Declaración de gravamen del usuario (VB-A-04). FLIT no consulta el registro de garantías. */
export interface TransferGravamenInput {
  gravamenActivo: boolean;
  tieneLevantamientoOAutorizacion: boolean;
}

/** Declaración de régimen aplicable (§4.0, CF-24). Se persiste; el bloqueo VB-07 llega después. */
export interface TransferRegimenInput {
  ningunaAplica?: boolean | null;
  condicionesDeclaradas?: string[];
  declaredAt?: string | null;
}

/**
 * Cuerpo de `POST /transferencia/generate`. `escenarios` es una LISTA porque VB-05 exige
 * «exactamente uno»: con un escalar, el caso «más de un escenario» no existiría.
 */
export interface TransferGenerateRequest {
  escenarios: StandaloneDocumentScenario[];
  vehiculo: TransferVehiculoInput;
  transferente: TransferParteInput;
  adquirente?: TransferParteInput;
  negocio: TransferNegocioInput;
  gravamen?: TransferGravamenInput;
  regimenAplicable?: TransferRegimenInput;
}

/**
 * Hallazgo de validación del anexo §6. El mensaje NUNCA repite el valor capturado: llega así del
 * backend y la interfaz tampoco lo reconstruye.
 */
export interface TransferValidationIssue {
  code: string;
  field: string;
  message: string;
}

/** Respuesta de la generación: id, estado y las prevalidaciones advisory (avisos, no errores). */
export interface TransferGenerateResult {
  id: string;
  status: StandaloneDocumentStatus;
  advisories: TransferValidationIssue[];
}
