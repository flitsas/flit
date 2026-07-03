/**
 * Tipos del módulo "Generación de improntas" (HU #10469, Feature #10462).
 *
 * Contrato de `POST /api/v1/admin/improntas/generate` documentado en el diseño técnico
 * del Feature #10462 (integración Kyverum RUNT): el backend responde con el binario
 * `application/pdf` listo para descarga (mismo patrón que `exportExecutivePdf` en
 * `lib/api/analytics.ts`).
 */
export interface GenerarImprontaRequest {
  /** Placa del vehículo. Obligatoria. */
  placa: string;
  /**
   * Documento de identidad del propietario del vehículo. Obligatorio — requerido por Kyverum RUNT
   * para toda consulta por placa (no documentado originalmente en el contrato del proveedor,
   * descubierto validando contra el proveedor real).
   */
  documento: string;
  /**
   * Número de motor. Opcional — verificado contra el proveedor real: Kyverum resuelve los
   * identificadores del vehículo directamente desde el RUNT vía placa+documento cuando no se
   * envían.
   */
  numMotor?: string;
  /** Número de chasis. Opcional, mismo hallazgo que numMotor. */
  numChasis?: string;
  /** Número de serie/VIN. Opcional, mismo hallazgo que numMotor. */
  numSerie?: string;
  /** Marca del vehículo. Opcional. */
  marca?: string;
  /** Línea del vehículo. Opcional. */
  linea?: string;
  /** Modelo (año) del vehículo. Opcional. */
  modelo?: string;
  /** Nombre de la organización solicitante. Pre-cargado desde el tenant en sesión. */
  orgNombre: string;
  /**
   * NIT de la organización solicitante. Opcional — verificado contra el proveedor real: Kyverum
   * genera la impronta sin este dato. Pre-cargado desde el tenant en sesión si está disponible.
   */
  orgNit?: string;
  /** Ciudad de la organización solicitante. Opcional, mismo hallazgo que orgNit. */
  orgCiudad?: string;
  /** Operador que radica la solicitud. Pre-cargado desde el usuario en sesión. */
  operador: string;
}

/**
 * Metadata de trazabilidad de una generación exitosa (HU #10471, AC1). El endpoint
 * `POST /generate` responde con el binario `application/pdf`; el radicado/hash — si el
 * backend los expone — viajan en headers `X-Impronta-Radicado`/`X-Impronta-Hash`.
 */
export interface GenerarImprontaResult {
  radicado: string | null;
  hash: string | null;
}

/**
 * Cuerpo de error esperado del backend ante 4xx/5xx del endpoint de generación
 * (HU #10471 AC2).
 */
export interface ImprontaErrorBody {
  code?: string;
  message?: string;
  errors?: Array<{ field?: string; message: string }>;
}

/**
 * Fila del historial de improntas generadas (HU #10470). Contrato de
 * `GET /api/v1/admin/improntas` (HU #10468 backend).
 */
export interface ImprontaHistorialItem {
  id: string;
  radicado: string;
  placa: string;
  numMotor?: string | null;
  numChasis?: string | null;
  numSerie?: string | null;
  marca?: string | null;
  linea?: string | null;
  modelo?: string | null;
  orgNombre: string;
  orgNit: string;
  orgCiudad: string;
  operador: string;
  hash: string;
  fechaImpresa: string;
  flitUserName: string;
  tenantId: string;
  createdAt: string;
}

/** Filtros de `GET /api/v1/admin/improntas` — placa y rango de fecha (AC3). */
export interface ImprontasHistorialParams {
  placa?: string;
  dateFrom?: string;
  dateTo?: string;
  page?: number;
  pageSize?: number;
}

export interface ImprontasHistorialPagedResult {
  data: ImprontaHistorialItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}
