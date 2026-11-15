// Rango de fechas del dashboard y de los reportes (contrato-api.md §0): AAAA-MM-DD en hora Colombia (UTC−5 fijo), días
// completos, desde ≤ hasta ≤ hoy y como máximo 366 días. El front valida antes de pedir para no gastar un 400; el
// servidor vuelve a validar (400 RANGO_INVALIDO).
import type { Rango } from "./tipos";

export const RANGO_MAXIMO_DIAS = 366;
export const DIAS_POR_DEFECTO = 30;
export const MENSAJE_RANGO_MAXIMO = "El rango no puede superar un año";
export const MENSAJE_HASTA_FUTURO = "La fecha final no puede ser posterior a hoy";
export const MENSAJE_HASTA_ANTERIOR = "La fecha final no puede ser anterior a la inicial";

export interface ErrorRango {
  campo: "desde" | "hasta";
  mensaje: string;
}

const FORMATO = /^\d{4}-\d{2}-\d{2}$/;
const DIA_MS = 86_400_000;
const FORMATO_BOGOTA = new Intl.DateTimeFormat("en-CA", { timeZone: "America/Bogota", year: "numeric", month: "2-digit", day: "2-digit" });

/** Día de hoy (AAAA-MM-DD) en Bogotá. */
export function hoyColombia(ahora: Date = new Date()): string {
  return FORMATO_BOGOTA.format(ahora);
}

/** Milisegundos UTC de la medianoche de una fecha AAAA-MM-DD; NaN si no es una fecha real. */
function aMs(fecha: string): number {
  if (!FORMATO.test(fecha)) return Number.NaN;
  const [a, m, d] = fecha.split("-").map(Number);
  const ms = Date.UTC(a, m - 1, d);
  const real = new Date(ms);
  return real.getUTCFullYear() === a && real.getUTCMonth() === m - 1 && real.getUTCDate() === d ? ms : Number.NaN;
}

export function esFechaValida(fecha: string | null | undefined): fecha is string {
  return typeof fecha === "string" && !Number.isNaN(aMs(fecha));
}

export function sumarDias(fecha: string, dias: number): string {
  return new Date(aMs(fecha) + dias * DIA_MS).toISOString().slice(0, 10);
}

/** Días del rango con los dos extremos incluidos. */
export function diasDelRango({ desde, hasta }: Rango): number {
  return Math.round((aMs(hasta) - aMs(desde)) / DIA_MS) + 1;
}

/** Los últimos `dias` días con hoy incluido. */
export function rangoPorDefecto(hoy: string, dias: number = DIAS_POR_DEFECTO): Rango {
  return { desde: sumarDias(hoy, -(dias - 1)), hasta: hoy };
}

/** Primer incumplimiento del rango, o null si se puede pedir. */
export function validarRango({ desde, hasta }: Rango, hoy: string): ErrorRango | null {
  if (!esFechaValida(desde)) return { campo: "desde", mensaje: "Elige una fecha inicial válida" };
  if (!esFechaValida(hasta)) return { campo: "hasta", mensaje: "Elige una fecha final válida" };
  if (aMs(hasta) > aMs(hoy)) return { campo: "hasta", mensaje: MENSAJE_HASTA_FUTURO };
  if (aMs(hasta) < aMs(desde)) return { campo: "hasta", mensaje: MENSAJE_HASTA_ANTERIOR };
  if (diasDelRango({ desde, hasta }) > RANGO_MAXIMO_DIAS) return { campo: "hasta", mensaje: MENSAJE_RANGO_MAXIMO };
  return null;
}

/** Rango de la URL (?desde=&hasta=) si es válido; si no, el de por defecto. */
export function leerRango(params: Pick<URLSearchParams, "get">, hoy: string): Rango {
  const rango = { desde: params.get("desde") ?? "", hasta: params.get("hasta") ?? "" };
  return validarRango(rango, hoy) ? rangoPorDefecto(hoy) : rango;
}
