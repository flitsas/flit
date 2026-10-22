// Datos ficticios del backoffice (HU #13802): compañía, lotes, grilla con origen y formularios del llenado manual inventados,
// con placas TST### y documentos ya enmascarados como los entrega la API. Nada sale de datos reales.
import type { Fuente } from "@/lib/configuracion";
import type { EstadoCelda } from "@/lib/tablero/api";
import type {
  CeldaGrillaBackoffice,
  DiagnosticoBackoffice,
  FilaVehiculoBackoffice,
  FormularioManual,
  GrillaBackoffice,
  ListaDiagnosticosBackoffice,
  PersonaEnError,
  RespuestaLlenado,
} from "@/lib/backoffice/api";

export { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";

/** Compañía A (cliente) elegida en el selector. */
export const EMPRESA_A = "00000000-0000-7000-8000-0000000000a1";
export const LOTE = "00000000-0000-7000-8000-00000000b001";
export const USUARIO_BACKOFFICE = "00000000-0000-7000-8000-0000000000f5";

export function celdaBo(vehiculo: number, fuente: Fuente, estado: EstadoCelda, cambios: Partial<CeldaGrillaBackoffice> = {}): CeldaGrillaBackoffice {
  return {
    id: `celda-${vehiculo}-${fuente.toLowerCase()}`,
    fuente,
    estado,
    motivo_na: null,
    tipo_error: null,
    dato_obtenido_en: estado === "COMPLETADO" ? "2026-10-22T15:00:00Z" : null,
    reutilizada: false,
    version: 10,
    origen: estado === "COMPLETADO" ? "API" : null,
    usuario_manual_id: null,
    ...cambios,
  };
}

export function vehiculoBo(n: number, celdas: CeldaGrillaBackoffice[]): FilaVehiculoBackoffice {
  const placa = `TST${String(n).padStart(3, "0")}`;
  return {
    id: `vehiculo-${n}`,
    orden: n,
    identificador: placa,
    tipo_identificador: "PLACA_CARRO",
    placa,
    vin: null,
    terminado: celdas.every((c) => c.estado === "COMPLETADO" || c.estado === "NA"),
    celdas,
  };
}

/**
 * Lote de A: TST001 con SIMIT vehículo en ERROR (llenable), avalúo en ERROR (llenable) y RUNT completado por API;
 * TST002 con RUNT en ERROR, su SIMIT propietario bloqueado por RUNT y el SIMIT vehículo ya llenado a mano.
 */
export function grillaBo(cambios: Partial<GrillaBackoffice> = {}): GrillaBackoffice {
  const vehiculos = cambios.vehiculos ?? [
    vehiculoBo(1, [
      celdaBo(1, "RUNT", "COMPLETADO"),
      celdaBo(1, "SIMIT_VEHICULO", "ERROR", { tipo_error: "TIMEOUT" }),
      celdaBo(1, "AVALUO", "ERROR", { tipo_error: "HTTP_5XX" }),
      celdaBo(1, "SIMIT_PROPIETARIO", "COMPLETADO"),
    ]),
    vehiculoBo(2, [
      celdaBo(2, "RUNT", "ERROR", { tipo_error: "TIMEOUT" }),
      celdaBo(2, "SIMIT_VEHICULO", "COMPLETADO", { origen: "MANUAL", usuario_manual_id: USUARIO_BACKOFFICE }),
      celdaBo(2, "AVALUO", "NA", { motivo_na: "SERVICIO_PUBLICO" }),
      celdaBo(2, "SIMIT_PROPIETARIO", "ERROR", { tipo_error: "BLOQUEADA_POR_RUNT" }),
    ]),
  ];
  return {
    empresa: EMPRESA_A,
    id: LOTE,
    nombre: "Lote octubre",
    estado: "INCOMPLETO",
    version: 20,
    desde: 0,
    columnas: { por_vehiculo: ["RUNT", "SIMIT_VEHICULO", "AVALUO"], por_propietario: ["SIMIT_PROPIETARIO"], por_locatario: [] },
    avance: { vehiculos: vehiculos.length, terminados: vehiculos.filter((v) => v.terminado).length },
    vehiculos,
    ...cambios,
  };
}

export function lote(n: number, cambios: Partial<DiagnosticoBackoffice> = {}): DiagnosticoBackoffice {
  return {
    id: `00000000-0000-7000-8000-00000000b${String(n).padStart(3, "0")}`,
    nombre: `Lote de prueba ${n}`,
    estado: "INCOMPLETO",
    creado_en: "2026-10-22T14:00:00+00:00",
    vehiculos: 2,
    celdas_en_error: 2,
    ...cambios,
  };
}

export function listaLotes(diagnosticos: DiagnosticoBackoffice[], cambios: Partial<ListaDiagnosticosBackoffice> = {}): ListaDiagnosticosBackoffice {
  return { empresa: EMPRESA_A, pagina: 1, tamano: 50, total: diagnosticos.length, diagnosticos, ...cambios };
}

export function formulario(fuente: Fuente, cambios: Partial<FormularioManual> = {}): FormularioManual {
  return {
    id: `celda-1-${fuente.toLowerCase()}`,
    diagnostico_id: LOTE,
    vehiculo_id: "vehiculo-1",
    fuente,
    estado: "ERROR",
    tipo_error: "TIMEOUT",
    version: 10,
    admite_manual: true,
    motivo: null,
    personas: null,
    ...cambios,
  };
}

export function personaEnError(orden: number, cambios: Partial<PersonaEnError> = {}): PersonaEnError {
  return {
    persona_id: `00000000-0000-7000-8000-00000000d${String(orden).padStart(3, "0")}`,
    orden,
    rol: "PROPIETARIO",
    tipo_documento: "C",
    documento_enmascarado: `****010${orden}`,
    declarado_por_cliente: false,
    consulta: "SIMIT",
    tipo_error: "TIMEOUT",
    ...cambios,
  };
}

export function respuestaLlenado(fuente: Fuente, cambios: Partial<RespuestaLlenado> = {}): RespuestaLlenado {
  return {
    id: `celda-1-${fuente.toLowerCase()}`,
    diagnostico_id: LOTE,
    vehiculo_id: "vehiculo-1",
    fuente,
    estado: "COMPLETADO",
    origen: "MANUAL",
    usuario_manual_id: USUARIO_BACKOFFICE,
    dato_obtenido_en: "2026-10-22T17:30:00+00:00",
    version: 21,
    estado_lote: "EN_PROCESO",
    ...cambios,
  };
}
