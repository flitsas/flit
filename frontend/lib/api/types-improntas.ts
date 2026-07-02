/**
 * Tipos del módulo "Generación de improntas" (HU #10469, Feature #10462).
 *
 * Contrato de `POST /api/v1/admin/improntas/generate` documentado en el diseño técnico
 * del Feature #10462 (integración Kyverum RUNT): el backend responde con el binario
 * `application/pdf` listo para descarga (mismo patrón que `exportExecutivePdf` en
 * `lib/api/analytics.ts`). El backend real de este endpoint se implementa en la HU
 * #10467 y aún no existe al momento de esta HU — el frontend consume el contrato ya
 * acordado para poder maquetar el formulario en paralelo.
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
 * Fila del historial de improntas generadas (HU #10470). Contrato de
 * `GET /api/v1/admin/improntas` documentado en el diseño técnico del Feature #10462
 * (tabla de trazabilidad `admin.impronta_generations`, ver HU #10468 backend, aún no
 * mergeada al momento de esta HU). `fechaImpresa` es la fecha impresa en el certificado
 * (devuelta por Kyverum); se usa como "fecha de generación" tanto para mostrar en tabla
 * como para el filtro de rango de fechas (AC1/AC3).
 */
export interface ImprontaHistorialItem {
  /** Identificador interno del registro de trazabilidad. */
  id: string;
  /** Radicado interno devuelto por Kyverum (ej. IMPR-XXXXXXXX). */
  radicado: string;
  /** Placa del vehículo. */
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
  /** Hash SHA-256 impreso en el certificado (verificación de autenticidad). */
  hash: string;
  /** Fecha impresa en el certificado (ISO 8601) — usada como "fecha de generación". */
  fechaImpresa: string;
  /** Nombre del usuario FLIT que generó la impronta. */
  flitUserName: string;
  /** Tenant FLIT que generó la impronta. */
  tenantId: string;
  /** Timestamp de creación del registro de trazabilidad (ISO 8601). */
  createdAt: string;
}

/** Filtros de `GET /api/v1/admin/improntas` — al menos placa y rango de fecha (AC3). */
export interface ImprontasHistorialParams {
  /** Filtro por placa (coincidencia exacta o parcial, a criterio del backend). */
  placa?: string;
  /** Límite inferior del rango de fecha (ISO 8601), inclusive. */
  dateFrom?: string;
  /** Límite superior del rango de fecha (ISO 8601), inclusive. */
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
