// Presentación del diagnóstico (paso 1). Módulo puro: convierte las respuestas del BFF en lo que se pinta. No guarda nada.
import type { ConsultaVehiculoRequest, Requisito, Solicitud, VehiculoDelTramite } from "@/lib/api/cliente";
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

/** Organismo aún no elegido en el paso 1 (el BFF guarda el guid vacío). */
export const ORGANISMO_SIN_ELEGIR = "00000000-0000-0000-0000-000000000000";

export const organismoElegido = (id?: string | null): boolean => Boolean(id && id !== ORGANISMO_SIN_ELEGIR);

/** Tipos con los que FLIT 2 consulta el vehículo por placa. */
export const TIPOS_PROPIETARIO = [
  { valor: "CC", etiqueta: "Cédula de ciudadanía" },
  { valor: "CE", etiqueta: "Cédula de extranjería" },
  { valor: "NIT", etiqueta: "NIT" },
  { valor: "PAS", etiqueta: "Pasaporte" },
] as const;

const conUnidad = (valor: string | null | undefined, unidad: string): string | null => {
  const limpio = valor?.trim();
  if (!limpio) return null;
  return limpio.toLowerCase().endsWith(unidad) ? limpio : `${limpio} ${unidad}`;
};

/**
 * Atributos que FLIT 2 muestra al consultar el vehículo. Los que no vienen se omiten.
 * Cilindraje y medidas llevan la misma unidad que la ficha del operador.
 */
export function datosTecnicos(
  vehiculo: VehiculoDelTramite | null,
  solicitud: Pick<Solicitud, "placa" | "vin">,
  propietario?: string | null,
): [string, string][] {
  const filas: [string, string | null | undefined][] = [
    ["Propietario Actual", propietario],
    ["Placa", vehiculo?.placa ?? solicitud.placa],
    ["Marca", vehiculo?.marca],
    ["Línea", vehiculo?.linea],
    ["Modelo", vehiculo?.modeloAno],
    ["Estado", vehiculo?.estado],
    ["VIN", vehiculo?.vin ?? solicitud.vin],
    ["Servicio", vehiculo?.tipoServicio],
    ["Nº Chasis", vehiculo?.numeroChasis],
    ["Color", vehiculo?.color],
    ["Combustible", vehiculo?.combustible],
    ["Nº Serie", vehiculo?.numeroSerie],
    ["Clase", vehiculo?.clase],
    ["Cilindraje", conUnidad(vehiculo?.cilindraje, "cc")],
    ["Capacidad", vehiculo?.capacidad],
    ["Ejes", vehiculo?.ejes],
    ["Carrocería", vehiculo?.carroceria],
    ["Nº Motor", vehiculo?.numeroMotor],
    ["Alto", conUnidad(vehiculo?.alto, "mm")],
    ["Largo", conUnidad(vehiculo?.largo, "mm")],
    ["Ancho", conUnidad(vehiculo?.ancho, "mm")],
    ["Llantas", vehiculo?.llantas],
  ];
  return filas.filter((f): f is [string, string] => Boolean(f[1]));
}

/**
 * Qué se manda a la consulta del vehículo del trámite: con VIN, solo el VIN; con placa, la placa y el documento del
 * propietario, con el mismo tipo y número que usa FLIT 2. Sin ninguno de los dos datos no hay consulta posible.
 */
export function entradaConsulta(
  solicitud: Pick<Solicitud, "placa" | "vin">,
  propietario: { tipo: string; numero: string } | undefined,
): Omit<ConsultaVehiculoRequest, "consentimiento" | "consentimientoVersion"> | null {
  if (solicitud.vin) return { vin: solicitud.vin };
  if (solicitud.placa && propietario?.numero) return { placa: solicitud.placa, propietario: { tipo: propietario.tipo || "CC", numero: propietario.numero } };
  return null;
}

export const consentimientoDeConsulta = { consentimiento: true, consentimientoVersion: CONSENTIMIENTO_VERSION } as const;

export const MENSAJE_RUNT_NO_RESPONDE = "No pudimos consultar el RUNT. Intenta de nuevo.";
export const MENSAJE_SIN_VEHICULO = "No encontramos el vehículo";
export const MENSAJE_ATENCION = "Para continuar con la radicación debes estar al día en pagos de SOAT, multas e impuestos vehiculares.";
