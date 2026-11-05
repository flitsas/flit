/**
 * Reglas de identificadores de vehículo en Diagnóstico (HU #13745, F02-H3).
 *
 * Copia de la regla de Trámites (`frontend/lib/validation/fieldRules.ts`), que manda por decisión del usuario
 * (2026-10-21). Diagnóstico no puede importar desde `frontend/`, así que los patrones y la limpieza se copian
 * tal cual; el test compara los literales con los de Trámites. La tabla
 * `contracts/fixtures/validacion/identificadores-vehiculo.json` fija el comportamiento de los tres lados
 * (Trámites, este archivo y el validador .NET de core-diagnostico).
 * Las consumen la carga masiva (#13754) y la captura express (#13755).
 *
 * Uso de ejemplo:
 *   validarIdentificador(" mc029554 ")        // → { normalizado: "MC029554", tipo: "PLACA_MAQUINARIA" }
 *   validarIdentificador("ABC-123")           // → { normalizado: "ABC123", tipo: "PLACA_CARRO" }
 *   validarLongitudIdentificador("AB1234567") // → { codigo: "EXCEDE_LONGITUD", clase: "PLACA", longitud: 9, maximo: 8, ... }
 */

/** VIN (ISO 3779): 17 caracteres sin I, O ni Q. No se valida el dígito verificador. */
export const VIN_PATTERN = /^[A-HJ-NPR-Z0-9]{17}$/;

/** Placa colombiana: carro `AAA123`, moto `AAA12A`/`AAA12`, remolque `R12345`/`S123456`, maquinaria `AA123456`. */
export const PLATE_PATTERN = /^([A-Z]{3}[0-9]{3}|[A-Z]{3}[0-9]{2}[A-Z]?|[RS][0-9]{5,6}|[A-Z]{2}[0-9]{6})$/;

export const LONGITUD_MAXIMA_VIN = 17;
export const LONGITUD_MAXIMA_PLACA = 8;

/** Mayúsculas y solo el charset de VIN, sin cortar. */
const limpiarVin = (v: string): string => v.toUpperCase().replace(/[^A-HJ-NPR-Z0-9]/g, "");
/** Mayúsculas y solo alfanumérico, sin cortar. */
const limpiarPlaca = (v: string): string => v.toUpperCase().replace(/[^A-Z0-9]/g, "");

/**
 * Limpieza visible de un identificador mientras se escribe (captura express, HU #13755): mayúsculas y solo A-Z y 0-9,
 * SIN recortar, para que una entrada demasiado larga se vea entera y `validarLongitudIdentificador` la rechace.
 */
export const limpiarIdentificador = (v: string | null | undefined): string => limpiarPlaca(v ?? "");

/** Igual que Trámites: deja solo el charset válido de VIN, en mayúsculas y tope de 17. */
export const sanitizeVin = (v: string): string => limpiarVin(v).slice(0, LONGITUD_MAXIMA_VIN);

/** Igual que Trámites: deja solo alfanumérico en mayúsculas, tope de 8 (maquinaria `AA123456`). */
export const sanitizePlate = (v: string): string => limpiarPlaca(v).slice(0, LONGITUD_MAXIMA_PLACA);

export type TipoIdentificador = "PLACA_CARRO" | "PLACA_MOTO" | "PLACA_REMOLQUE" | "PLACA_MAQUINARIA" | "VIN";

export interface ResultadoIdentificador {
  normalizado: string;
  /** `null` = INVALIDO: ni placa ni VIN. */
  tipo: TipoIdentificador | null;
}

// Ramas de PLATE_PATTERN por separado, en el mismo orden que el validador .NET, para saber cuál casó.
const RAMAS_DE_PLACA: ReadonlyArray<readonly [TipoIdentificador, RegExp]> = [
  ["PLACA_CARRO", /^[A-Z]{3}[0-9]{3}$/],
  ["PLACA_MOTO", /^[A-Z]{3}[0-9]{2}[A-Z]?$/],
  ["PLACA_REMOLQUE", /^[RS][0-9]{5,6}$/],
  ["PLACA_MAQUINARIA", /^[A-Z]{2}[0-9]{6}$/],
];

/**
 * Normaliza y clasifica igual que Trámites: primero VIN (`sanitizeVin` + `VIN_PATTERN`); si no, placa
 * (`sanitizePlate` + ramas de `PLATE_PATTERN`). Si nada casa es INVALIDO y el normalizado es el valor en
 * mayúsculas con solo A-Z y 0-9, sin cortar. Ojo: el corte a 17/8 puede volver válida una entrada demasiado
 * larga; en carga masiva y express usa además `validarLongitudIdentificador`.
 */
export function validarIdentificador(identificador: string | null | undefined): ResultadoIdentificador {
  const entrada = identificador ?? "";

  const vin = sanitizeVin(entrada);
  if (VIN_PATTERN.test(vin)) return { normalizado: vin, tipo: "VIN" };

  const placa = sanitizePlate(entrada);
  const rama = RAMAS_DE_PLACA.find(([, patron]) => patron.test(placa));
  if (rama && PLATE_PATTERN.test(placa)) return { normalizado: placa, tipo: rama[0] };

  return { normalizado: limpiarPlaca(entrada), tipo: null };
}

export interface ErrorLongitudIdentificador {
  codigo: "EXCEDE_LONGITUD";
  clase: "PLACA" | "VIN";
  longitud: number;
  maximo: number;
  mensaje: string;
}

/**
 * Regla de Diagnóstico para carga masiva y express (decisión del usuario 2026-10-21), aparte del validador
 * compartido: si el valor LIMPIO, antes de recortar, excede 17 en un VIN u 8 en una placa, la entrada se
 * rechaza con `EXCEDE_LONGITUD` en vez de recortarse en silencio. Devuelve `null` si no hay exceso (incluye
 * las entradas INVALIDO, que ya se rechazan por formato).
 */
export function validarLongitudIdentificador(
  identificador: string | null | undefined,
): ErrorLongitudIdentificador | null {
  const entrada = identificador ?? "";
  const { tipo } = validarIdentificador(entrada);
  if (tipo === null) return null;

  if (tipo === "VIN") {
    const longitud = limpiarVin(entrada).length;
    if (longitud <= LONGITUD_MAXIMA_VIN) return null;
    return {
      codigo: "EXCEDE_LONGITUD",
      clase: "VIN",
      longitud,
      maximo: LONGITUD_MAXIMA_VIN,
      mensaje: `El VIN tiene ${longitud} caracteres y el máximo es ${LONGITUD_MAXIMA_VIN}. Corrígelo; no se recorta.`,
    };
  }

  const longitud = limpiarPlaca(entrada).length;
  if (longitud <= LONGITUD_MAXIMA_PLACA) return null;
  return {
    codigo: "EXCEDE_LONGITUD",
    clase: "PLACA",
    longitud,
    maximo: LONGITUD_MAXIMA_PLACA,
    mensaje: `La placa tiene ${longitud} caracteres y el máximo es ${LONGITUD_MAXIMA_PLACA}. Corrígela; no se recorta.`,
  };
}
