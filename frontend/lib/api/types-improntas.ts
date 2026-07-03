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
 * Metadata de trazabilidad de una generación exitosa (HU #10471, AC1). El endpoint
 * `POST /generate` responde con el binario `application/pdf` (no puede llevar JSON en
 * el body), así que el radicado/hash — si el backend los expone — viajan en los headers
 * de respuesta `X-Impronta-Radicado`/`X-Impronta-Hash` (ver `admin-improntas.ts`).
 *
 * Limitación conocida: al momento de esta HU, el endpoint real de la HU #10467 (Kyverum
 * RUNT) todavía no está implementado, por lo que este contrato de headers es una
 * propuesta del frontend, no un acuerdo confirmado con `architecture-agent`/backend. Si
 * el backend no envía esos headers (p. ej. por no exponerlos vía CORS
 * `Access-Control-Expose-Headers`, o por no implementarlos), ambos campos llegan en
 * `null` y el formulario muestra un mensaje de éxito genérico — no es un error.
 */
export interface GenerarImprontaResult {
  radicado: string | null;
  hash: string | null;
}

/**
 * Cuerpo de error esperado del backend ante 4xx/5xx del endpoint de generación
 * (notas técnicas del AC2 de la HU #10471): `code` distingue `VALIDATION_ERROR`
 * (422), `UNAUTHORIZED` (401) y `UPSTREAM_UNAVAILABLE` (502). `errors` es opcional y
 * solo se usa para enriquecer el mensaje de `VALIDATION_ERROR` con el detalle de
 * campo, si el backend lo entrega (mismo shape que `ValidationErrorResponse`).
 */
export interface ImprontaErrorBody {
  code?: string;
  message?: string;
  errors?: Array<{ field?: string; message: string }>;
}
