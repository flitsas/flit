// Datos ficticios de las pruebas de la pantalla 10 (HU #13744): compañía, sesión y configuración inventadas.
import type { Configuracion, ContratoBackoffice } from "@/lib/configuracion";
import type { CapacidadesDiagnostico, SesionDiagnostico } from "@/lib/sesion";

export const EMPRESA = "00000000-0000-7000-8000-0000000000e1";
export const EMPRESA_CLIENTE = "00000000-0000-7000-8000-0000000000c2";

export function configuracion(
  cambios: { contrato?: Partial<Configuracion["contrato"]>; operacion?: Partial<Configuracion["operacion"]> } = {},
): Configuracion {
  return {
    empresa: EMPRESA,
    contrato: {
      solo_lectura: true,
      fuentes_habilitadas: ["RUNT", "SIMIT_VEHICULO", "AVALUO"],
      limite_vehiculos_lote: 500,
      actualizado_en: null,
      ...cambios.contrato,
    },
    operacion: {
      visibilidad_usuario: "EMPRESA",
      fuentes_por_defecto: ["RUNT"],
      estimado_consultas: "NO_MOSTRAR",
      actualizado_por: null,
      actualizado_en: null,
      ...cambios.operacion,
    },
  };
}

export function contratoBackoffice(cambios: Partial<ContratoBackoffice> = {}): ContratoBackoffice {
  return {
    empresa: EMPRESA_CLIENTE,
    fuentes_habilitadas: ["RUNT", "SIMIT_VEHICULO"],
    limite_vehiculos_lote: 300,
    redaccion_ia_suspendida: false,
    fuentes_por_defecto: ["RUNT"],
    actualizado_por: null,
    actualizado_en: null,
    ...cambios,
  };
}

const SIN_CAPACIDADES: CapacidadesDiagnostico = {
  crearDiagnosticos: false,
  verDiagnosticos: false,
  descargarReportes: false,
  derivar: false,
  verConfiguracion: false,
  editarConfiguracion: false,
  reprocesarConsultas: false,
  backoffice: false,
  editarContrato: false,
  llenarConsultas: false,
  corregirDictamen: false,
  verAuditoria: false,
  verVolumetria: false,
};

export function sesion(capacidades: Partial<CapacidadesDiagnostico>, tipo = "COMPANY"): SesionDiagnostico {
  return {
    usuario: { id: "00000000-0000-7000-8000-000000000001", email: "ana@empresa-prueba.co", esSuperAdmin: false },
    empresa: { id: EMPRESA, nombre: "Transportes Prueba SAS", tipo },
    roles: [],
    permisos: [],
    capacidades: { ...SIN_CAPACIDADES, ...capacidades },
  };
}

export const ADMIN: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, verConfiguracion: true, editarConfiguracion: true };
export const USUARIO: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, verConfiguracion: true };
export const BACKOFFICE: Partial<CapacidadesDiagnostico> = { verConfiguracion: true, backoffice: true, editarContrato: true };

/** Cuerpo JSON de la llamada n-ésima al fetch simulado. */
export function cuerpo(fetchMock: { mock: { calls: unknown[][] } }, n: number): unknown {
  const init = fetchMock.mock.calls[n][1] as RequestInit | undefined;
  return init?.body ? JSON.parse(String(init.body)) : undefined;
}
