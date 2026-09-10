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
 * Fecha de CALENDARIO pura (`AAAA-MM-DD`), sin hora ni zona: es lo que serializa un `DateOnly` del
 * backend (vigencias de escrituras, del baúl de firmas y de identidad).
 */
const FECHA_CALENDARIO = /^(\d{4})-(\d{2})-(\d{2})$/;

/** ¿El día del match existe realmente? Descarta meses y días fuera de rango (30/02, 13/45…). */
function esFechaReal([, year, month, day]: RegExpExecArray): boolean {
  const y = Number(year);
  const m = Number(month);
  const d = Number(day);
  const utc = new Date(Date.UTC(y, m - 1, d));
  return (
    utc.getUTCFullYear() === y && utc.getUTCMonth() === m - 1 && utc.getUTCDate() === d
  );
}

/**
 * Fecha de negocio: `AAAA/MM/DD`, sin hora. Devuelve `fallback` si el valor no es una fecha válida
 * (el listado no debe romperse por un dato corrupto).
 *
 * HU #11194 — una fecha de calendario NO se convierte de zona. `new Date("2026-07-01")` la
 * interpreta como medianoche UTC y, al formatearla en Bogotá (UTC−5), sale el día anterior:
 * `2026/06/30`. Por eso toda vigencia se veía un día antes en consulta mientras la edición
 * (que recibe la cadena cruda en un `<input type="date">`) mostraba la correcta.
 * Los instantes (`timestamptz`) sí se siguen convirtiendo a la hora de Colombia.
 */
export function formatFecha(value: string | Date | null | undefined, fallback = "—"): string {
  if (value === null || value === undefined || value === "") return fallback;

  if (typeof value === "string") {
    const calendario = FECHA_CALENDARIO.exec(value.trim());
    // La forma `AAAA-MM-DD` no basta: "2026-13-45" la cumple y no es una fecha. Se comprueba que el
    // día exista de verdad; si no, cae al camino general y termina en el respaldo, como antes.
    if (calendario && esFechaReal(calendario)) {
      const [, year, month, day] = calendario;
      return `${year}/${month}/${day}`;
    }
  }

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
