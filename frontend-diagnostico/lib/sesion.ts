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
  /**
   * Saneamiento (plan §5.3): marcar, desmarcar y pedir varios (permiso `diagnostico.saneamiento.solicitar`; el SuperAdmin
   * también, decisión E3). Opcional: un back anterior no la manda y cuenta como false (`puedeSanear`).
   */
  sanear?: boolean;
  /** Saneamiento (plan §5.3): bandeja y gestión FLIT del backoffice. Opcional; ausente = false (`puedeGestionarSaneamiento`). */
  gestionarSaneamiento?: boolean;
  /**
   * Ver reportes de la compañía en pantalla (permiso `diagnostico.reportes.ver`, contrato 1.5.0 SP-2). Exportar sigue con
   * `descargarReportes`. Opcional: un back anterior no la manda y cuenta como false (`puedeVerReportes`).
   */
  verReportes?: boolean;
  /**
   * Vista global del Dashboard y de los reportes con selector de compañía (backoffice + `diagnostico.volumetria.ver`,
   * contrato 1.5.0 SP-2, decisión Q4). Opcional; ausente = false (`tieneVistaGlobal`).
   */
  verEstadisticasGlobales?: boolean;
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

/** Bandera de saneamiento: solo con `sanear` en true (ausente = false). La API vuelve a exigir el permiso. */
export function puedeSanear(capacidades: CapacidadesDiagnostico | null | undefined): boolean {
  return capacidades?.sanear === true;
}

/** Bandeja y gestión de saneamientos del backoffice: solo con `gestionarSaneamiento` en true (ausente = false). */
export function puedeGestionarSaneamiento(capacidades: CapacidadesDiagnostico | null | undefined): boolean {
  return capacidades?.gestionarSaneamiento === true;
}

/** Vista global (Dashboard y Reportes con «Todas las compañías»): solo con `verEstadisticasGlobales` en true. */
export function tieneVistaGlobal(capacidades: CapacidadesDiagnostico | null | undefined): boolean {
  return capacidades?.verEstadisticasGlobales === true;
}

/** Entrada «Reportes» y página /reportes: con `verReportes` (compañía) o con la vista global (rutas del backoffice). */
export function puedeVerReportes(capacidades: CapacidadesDiagnostico | null | undefined): boolean {
  return capacidades?.verReportes === true || tieneVistaGlobal(capacidades);
}
