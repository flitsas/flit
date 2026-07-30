// Catálogo canónico de tipos de trámite. La consola documental y el listado SuperAdmin
// resuelven ids desde el API (`GET /api/v1/superadmin/procedure-types`); esta lista es solo
// fallback/documentación para tests y demos locales. Códigos alineados a
// `CreateProcedureInstanceHandler` (MATRICULA_NUEVA / TRASPASO_STANDARD) y al seed
// `40-catalogo-tipos-tramite-canonico.sql`.
export interface ProcedureTypeOption {
  id: string;
  code: string;
  name: string;
}

/** Tipos activos del catálogo canónico (sin los inactivos). */
export const PROCEDURE_TYPES: ProcedureTypeOption[] = [
  { id: "canonical-matricula-nueva", code: "MATRICULA_NUEVA", name: "Matrícula inicial" },
  { id: "canonical-traspaso-standard", code: "TRASPASO_STANDARD", name: "Traspaso" },
  { id: "canonical-cambio-locatario", code: "CAMBIO_LOCATARIO", name: "Cambio de locatario" },
  { id: "canonical-cambio-carroceria", code: "CAMBIO_CARROCERIA", name: "Cambio de carrocería" },
  { id: "canonical-blindaje", code: "BLINDAJE", name: "Blindaje" },
  { id: "canonical-cambio-color", code: "CAMBIO_COLOR", name: "Cambio de color" },
  { id: "canonical-duplicado-placa", code: "DUPLICADO_PLACA", name: "Duplicado de placa" },
  { id: "canonical-duplicado-tarjeta", code: "DUPLICADO_TARJETA", name: "Duplicado de tarjeta" },
  { id: "canonical-levantamiento-prenda", code: "LEVANTAMIENTO_PRENDA", name: "Levantar prenda" },
  { id: "canonical-prenda-inscripcion", code: "PRENDA_INSCRIPCION", name: "Inscribir prenda" },
  { id: "canonical-radicado-cuenta", code: "RADICADO_CUENTA", name: "Radicado de cuenta" },
  { id: "canonical-conversion-combustible", code: "CONVERSION_COMBUSTIBLE", name: "Conversiones de combustible" },
  { id: "canonical-traslado-cuenta", code: "TRASLADO_CUENTA", name: "Traslado de cuenta" },
  { id: "canonical-cancelacion-matricula", code: "CANCELACION_MATRICULA", name: "Cancelación de matrícula" },
];

/** Devuelve el tipo de trámite por id, o `undefined` si no está en la lista estática. */
export function findProcedureType(id: string): ProcedureTypeOption | undefined {
  return PROCEDURE_TYPES.find((p) => p.id === id);
}
