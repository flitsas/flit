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
  backoffice: boolean;
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
