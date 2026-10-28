// Presentación del diagnóstico (paso 1). Módulo puro: convierte las respuestas del BFF en lo que se pinta. No guarda nada.
import type { ConsultaVehiculoRequest, Requisito, Solicitud, VehiculoMinimo } from "@/lib/api/cliente";
import { CONSENTIMIENTO_VERSION } from "@/lib/consentimiento";
import type { TonoBadge } from "@/lib/tramite/tonos";

export interface FilaDiagnostico {
  clave: string;
  nombre: string;
  tono: TonoBadge;
  /** Texto de la insignia. */
  estado: string;
}

/**
 * Filas del diagnóstico a partir de los requisitos que devuelve el preflight del tipo. Un requisito que no aplica al tipo no se
 * muestra. Tonos del diseño (verde, amarillo): el diseño no tiene tono rojo, así que lo que falta se ve amarillo y el aviso de
 * atención explica el bloqueo.
 */
export function filasDiagnostico(requisitos: readonly Requisito[]): FilaDiagnostico[] {
  return requisitos
    .filter((r) => r.estado !== "no_aplica")
    .map((r) => (r.estado === "cumple" ? { clave: r.codigo, nombre: r.descripcion, tono: "success", estado: "OK" } : { clave: r.codigo, nombre: r.descripcion, tono: "warning", estado: "POR REVISAR" }));
}

/** Datos del vehículo que el BFF autoriza para el ciudadano anónimo (dato mínimo); los que no vienen no se muestran. */
export function datosTecnicos(vehiculo: VehiculoMinimo | null, solicitud: Pick<Solicitud, "placa" | "vin">): [string, string][] {
  const filas: [string, string | null | undefined][] = [
    ["Placa", vehiculo?.placa ?? solicitud.placa],
    ["VIN", solicitud.vin],
    ["Servicio", vehiculo?.tipoServicio],
    ["Clase", vehiculo?.clase],
    ["Marca", vehiculo?.marca],
    ["Línea", vehiculo?.linea],
    ["Modelo", vehiculo?.modeloAno],
    ["Estado", vehiculo?.estado],
  ];
  return filas.filter((f): f is [string, string] => Boolean(f[1]));
}

/**
 * Qué se manda a POST /consultas/vehiculo: con VIN, solo el VIN; con placa, la placa y el documento del propietario (la cédula con
 * la que se abrió el trámite). Sin ninguno de los dos datos no hay consulta posible.
 */
export function entradaConsulta(solicitud: Pick<Solicitud, "placa" | "vin">, cedula: string | undefined): Omit<ConsultaVehiculoRequest, "consentimiento" | "consentimientoVersion"> | null {
  if (solicitud.vin) return { vin: solicitud.vin };
  if (solicitud.placa && cedula) return { placa: solicitud.placa, propietario: { tipo: "CC", numero: cedula } };
  return null;
}

export const consentimientoDeConsulta = { consentimiento: true, consentimientoVersion: CONSENTIMIENTO_VERSION } as const;

export const MENSAJE_RUNT_NO_RESPONDE = "No pudimos consultar el RUNT. Intenta de nuevo.";
export const MENSAJE_SIN_VEHICULO = "No encontramos el vehículo";
export const MENSAJE_ATENCION = "Para continuar con la radicación debes estar al día en pagos de SOAT, multas e impuestos vehiculares.";
