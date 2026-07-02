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
  /** Número de motor. Al menos uno de numMotor/numChasis/numSerie es obligatorio. */
  numMotor?: string;
  /** Número de chasis. Al menos uno de numMotor/numChasis/numSerie es obligatorio. */
  numChasis?: string;
  /** Número de serie/VIN. Al menos uno de numMotor/numChasis/numSerie es obligatorio. */
  numSerie?: string;
  /** Marca del vehículo. Opcional. */
  marca?: string;
  /** Línea del vehículo. Opcional. */
  linea?: string;
  /** Modelo (año) del vehículo. Opcional. */
  modelo?: string;
  /** Nombre de la organización solicitante. Pre-cargado desde el tenant en sesión. */
  orgNombre: string;
  /** NIT de la organización solicitante. Pre-cargado desde el tenant en sesión. */
  orgNit: string;
  /** Ciudad de la organización solicitante. Pre-cargado desde el tenant en sesión. */
  orgCiudad: string;
  /** Operador que radica la solicitud. Pre-cargado desde el usuario en sesión. */
  operador: string;
}
