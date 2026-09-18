// Formato de fecha ÚNICO del producto — Épica #12552.
//
// Dos funciones, y la distinción entre ellas es el fondo del asunto:
//
//   · Un INSTANTE (`timestamptz` del backend: cuándo se radicó, cuándo cambió de estado) se
//     muestra con hora y se convierte a la hora de Colombia → `formatFechaHora`.
//   · Una FECHA DE CALENDARIO (`DateOnly`: hasta cuándo vale una escritura, un SOAT, una
//     identidad) NO tiene hora y NO se convierte de zona → `formatFechaCalendario`.
//
// Esto SUSTITUYE el criterio de la HU #11018, que mostraba las fechas de negocio como
// `AÑO/MES/DÍA` sin hora. Es un cambio de criterio del negocio, no la corrección de un defecto:
// queda anotado en el Discussion de la Épica para que no compute contra aquella historia.

/**
 * Zona horaria de operación (Colombia).
 *
 * HU #12663 — se exporta para que NINGÚN punto de la app formatee un instante sin fijarla. Sin
 * `timeZone`, `toLocaleString` e `Intl.DateTimeFormat` usan la zona del NAVEGADOR: quien se
 * conecte desde fuera de Colombia ve horas que no son las del trámite.
 *
 * Solo aplica a INSTANTES. Ver `formatFechaCalendario` para el otro caso.
 */
export const ZONA_COLOMBIA = "America/Bogota";

const DIA = new Intl.DateTimeFormat("es-CO", {
  timeZone: ZONA_COLOMBIA,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
});

const HORA = new Intl.DateTimeFormat("es-CO", {
  timeZone: ZONA_COLOMBIA,
  hour: "2-digit",
  minute: "2-digit",
  hour12: false,
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
  return utc.getUTCFullYear() === y && utc.getUTCMonth() === m - 1 && utc.getUTCDate() === d;
}

/** `AAAA-MM-DD` válido → `DD/MM/YYYY` sin tocar la zona. `null` si no tiene esa forma. */
function calendarioLiteral(value: string | Date | null | undefined): string | null {
  if (typeof value !== "string") return null;
  const m = FECHA_CALENDARIO.exec(value.trim());
  // La forma `AAAA-MM-DD` no basta: "2026-13-45" la cumple y no es una fecha.
  if (!m || !esFechaReal(m)) return null;
  const [, year, month, day] = m;
  return `${day}/${month}/${year}`;
}

/** Día calendario de Colombia del instante dado, como `DD/MM/YYYY`. */
function diaEnColombia(date: Date): string {
  // `formatToParts` evita depender del orden que el locale imponga al patrón.
  const parts = DIA.formatToParts(date);
  const get = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((p) => p.type === type)?.value ?? "";
  return `${get("day")}/${get("month")}/${get("year")}`;
}

/**
 * Fecha de calendario: `DD/MM/YYYY`, **sin hora**. Excepción RN-08 de la Épica #12552, para lo que
 * no es un instante: vigencias de escritura, del baúl de firmas, de identidad, de SOAT y de RTM.
 *
 * HU #11194 — una fecha de calendario NO se convierte de zona. `new Date("2026-07-01")` la
 * interpreta como medianoche UTC y, al formatearla en Bogotá (UTC−5), sale el día anterior:
 * `30/06/2026`. Por eso toda vigencia se veía un día antes en consulta mientras la edición
 * (que recibe la cadena cruda en un `<input type="date">`) mostraba la correcta.
 *
 * Devuelve `fallback` si el valor no es una fecha válida: un listado no debe romperse por un dato
 * corrupto.
 */
export function formatFechaCalendario(
  value: string | Date | null | undefined,
  fallback = "—",
): string {
  if (value === null || value === undefined || value === "") return fallback;

  const literal = calendarioLiteral(value);
  if (literal) return literal;

  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return fallback;

  return diaEnColombia(date);
}

/**
 * Instante: `DD/MM/YYYY HH:mm` en hora de Colombia. Es el formato estándar de la plataforma
 * (Épica #12552, RN-02) para todo lo que representa un momento en el tiempo.
 *
 * Sin segundos por decisión del negocio: ningún punto de la plataforma los mostraba y no aportan a
 * la lectura operativa de un trámite.
 *
 * Si recibe una fecha de CALENDARIO (`AAAA-MM-DD`) devuelve `DD/MM/YYYY` sin hora, en vez de
 * inventarle una. Es una llamada equivocada —lo suyo es `formatFechaCalendario`— pero convertirla
 * la correría un día atrás, y mostrar una fecha correcta y más corta es preferible a mostrar una
 * incorrecta.
 */
export function formatFechaHora(
  value: string | Date | null | undefined,
  fallback = "—",
): string {
  if (value === null || value === undefined || value === "") return fallback;

  const literal = calendarioLiteral(value);
  if (literal) return literal;

  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return fallback;

  return `${diaEnColombia(date)} ${HORA.format(date)}`;
}
