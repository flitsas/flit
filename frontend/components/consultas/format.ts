import { formatFechaHora } from "@/lib/format/date";
// Formateo compartido por las dos consolas de consultas.

//
// Vive fuera de cada módulo porque un informe que dice «1.284» y otro que dice «1284» sobre el
// mismo dato se leen como dos productos. Todo lo de fecha va en huso de Bogotá, que es el mismo con
// el que el backend agrupa: si aquí se formateara en local, un trámite de las 11 de la noche
// aparecería con la fecha del día siguiente para quien abriera el informe desde otro huso.

const intFmt = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 0 });
const numFmt = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 1 });

export function formatInt(value: number | null | undefined): string {
  return value === null || value === undefined || Number.isNaN(value) ? "\u2014" : intFmt.format(value);
}

/** «3 tramites», «1 tramite». El numero formateado y la palabra que le toca, en una sola pieza. */
export function plural(count: number, singular: string, many: string): string {
  return `${formatInt(count)} ${count === 1 ? singular : many}`;
}

export function formatDays(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "\u2014";
  return `${numFmt.format(value)} ${value === 1 ? "d\u00eda" : "d\u00edas"}`;
}

/**
 * Fecha y hora de un instante, en el formato estándar de la plataforma (Épica #12552):
 * DD/MM/YYYY HH:mm en hora de Colombia.
 *
 * Antes existía también `formatDate`, que mostraba solo el día. Con el formato único las dos
 * convergieron, y mantener dos nombres para el mismo resultado invitaba al error: sus llamadas
 * pasaron a esta.
 */
export function formatDateTime(iso: string | null): string {
  if (!iso) return "\u2014";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "\u2014";
  return formatFechaHora(date);
}
