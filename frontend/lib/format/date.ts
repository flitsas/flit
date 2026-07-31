// Formato de fecha ÚNICO del producto (HU #11018).
//
// Regla acordada: en documentos generados y tablas de NEGOCIO la fecha se muestra como AÑO/MES/DÍA,
// sin hora. Las bitácoras técnicas (webhooks, logs de integración, línea de tiempo del trámite)
// CONSERVAN la hora: ahí el minuto es información de diagnóstico, no ruido.

/** Zona horaria de operación (Colombia). Fija el día calendario del que habla el negocio. */
const TZ = "America/Bogota";

const FECHA_CORTA = new Intl.DateTimeFormat("es-CO", {
  timeZone: TZ,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
});

/**
 * Fecha de negocio: `AAAA/MM/DD`, sin hora. Devuelve `fallback` si el valor no es una fecha válida
 * (el listado no debe romperse por un dato corrupto).
 */
export function formatFecha(value: string | Date | null | undefined, fallback = "—"): string {
  if (value === null || value === undefined || value === "") return fallback;

  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return fallback;

  // `formatToParts` evita depender del orden que el locale imponga al patrón.
  const parts = FECHA_CORTA.formatToParts(date);
  const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value ?? "";

  return `${get("year")}/${get("month")}/${get("day")}`;
}

/**
 * Fecha CON hora, para bitácoras y trazas técnicas. Se mantiene aparte a propósito: no es el formato
 * de negocio y no debe usarse en documentos ni en tablas operativas.
 */
export function formatFechaHora(value: string | Date | null | undefined, fallback = "—"): string {
  if (value === null || value === undefined || value === "") return fallback;

  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return fallback;

  return `${formatFecha(date)} ${new Intl.DateTimeFormat("es-CO", {
    timeZone: TZ,
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).format(date)}`;
}
