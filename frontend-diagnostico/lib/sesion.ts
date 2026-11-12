// Sesión de Diagnóstico tal como la entrega la API (GET /api/v1/diagnostico/sesion, contrato
// contracts/openapi/diagnostico.v1.yaml, esquema `Sesion`). El menú y los botones se arman con esto; la API vuelve a
// validar cada permiso.
export interface CapacidadesDiagnostico {
  crearDiagnosticos: boolean;
  verDiagnosticos: boolean;
  descargarReportes: boolean;
  derivar: boolean;
  verConfiguracion: boolean;
  editarConfiguracion: boolean;
  reprocesarConsultas: boolean;
  /** Abre el backoffice (resumen, contrato, diagnósticos, fuentes obligatorias, intentos): SuperAdmin de cualquier compañía o FLIT con permiso. */
  backoffice: boolean;
  /**
   * Operación FLIT del backoffice: override del dictamen y llenado manual (solo compañía FLIT con permiso). Un back anterior
   * a la ola 2026-11-11 no la manda: ausente cuenta como false (`puedeOperarBackoffice`).
   */
  backofficeOperacion?: boolean;
  editarContrato: boolean;
  llenarConsultas: boolean;
  corregirDictamen: boolean;
  verAuditoria: boolean;
  verVolumetria: boolean;
}

export interface SesionDiagnostico {
  usuario: { id: string; email: string; esSuperAdmin: boolean };
  empresa: { id: string; nombre?: string | null; tipo?: string | null };
  roles: string[];
  permisos: string[];
  capacidades: CapacidadesDiagnostico;
}

/** Override del dictamen y llenado manual del backoffice: solo con `backofficeOperacion` en true (ausente = false). */
export function puedeOperarBackoffice(capacidades: CapacidadesDiagnostico | null | undefined): boolean {
  return capacidades?.backofficeOperacion === true;
}
