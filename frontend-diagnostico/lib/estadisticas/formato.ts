// Formato es-CO de las cifras del dashboard y de los reportes: separador de miles, porcentajes con un decimal y tiempos en
// segundos. Un valor nulo sale «sin datos» (nunca «NaN» ni «0» inventado).
import type { Granularidad } from "./tipos";

const ENTERO = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 0 });
const UN_DECIMAL = new Intl.NumberFormat("es-CO", { minimumFractionDigits: 1, maximumFractionDigits: 1 });

export const SIN_DATOS = "sin datos";

export function formatoEntero(valor: number | null | undefined): string {
  return valor == null || !Number.isFinite(valor) ? SIN_DATOS : ENTERO.format(valor);
}

/** 0..1 → «97,2 %». */
export function formatoPorcentaje(valor: number | null | undefined): string {
  return valor == null || !Number.isFinite(valor) ? SIN_DATOS : `${UN_DECIMAL.format(valor * 100)} %`;
}

/** Porcentaje entero de una parte sobre un total (0 si el total es 0): «75 %». */
export function porcentajeDe(parte: number, total: number): string {
  return `${total > 0 ? Math.round((parte / total) * 100) : 0} %`;
}

/** Milisegundos → «2,3 s». */
export function formatoSegundos(ms: number | null | undefined): string {
  return ms == null || !Number.isFinite(ms) ? SIN_DATOS : `${UN_DECIMAL.format(ms / 1000)} s`;
}

const MESES = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

/** Rótulo de una cubeta de la serie: «14/11», «sem. 10/11» o «nov 2026». */
export function rotuloCubeta(inicio: string, granularidad: Granularidad): string {
  const [a, m, d] = inicio.split("-");
  if (granularidad === "MES") return `${MESES[Number(m) - 1] ?? m} ${a}`;
  const corto = `${d}/${m}`;
  return granularidad === "SEMANA" ? `sem. ${corto}` : corto;
}

export const TEXTO_GRANULARIDAD: Record<Granularidad, string> = { DIA: "por día", SEMANA: "por semana", MES: "por mes" };
