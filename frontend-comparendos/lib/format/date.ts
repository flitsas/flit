// Fechas siempre en la hora de Colombia, sin importar dónde corra el navegador.
export const ZONA_COLOMBIA = "America/Bogota";

const DIA = new Intl.DateTimeFormat("es-CO", { timeZone: ZONA_COLOMBIA, day: "2-digit", month: "short", year: "numeric" });
const DIA_HORA = new Intl.DateTimeFormat("es-CO", {
  timeZone: ZONA_COLOMBIA,
  day: "2-digit",
  month: "short",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
});

/** «2026-10-09» (fecha sin hora de la API) → «09 oct 2026». Una fecha sin hora no se corre de día por la zona. */
export function formatoFecha(valor: string | null | undefined): string {
  if (!valor) return "—";
  const soloFecha = /^\d{4}-\d{2}-\d{2}$/.test(valor);
  const fecha = soloFecha ? new Date(`${valor}T12:00:00-05:00`) : new Date(valor);
  return Number.isNaN(fecha.getTime()) ? "—" : DIA.format(fecha);
}

/** Instante ISO de la API → «09 oct 2026, 07:00 a. m.» en hora de Colombia. */
export function formatoFechaHora(valor: string | null | undefined): string {
  if (!valor) return "—";
  const fecha = new Date(valor);
  return Number.isNaN(fecha.getTime()) ? "—" : DIA_HORA.format(fecha);
}

const PESOS = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export function formatoPesos(valor: number | null | undefined): string {
  return valor === null || valor === undefined ? "—" : PESOS.format(valor);
}

const ISO_DIA = new Intl.DateTimeFormat("en-CA", { timeZone: ZONA_COLOMBIA, year: "numeric", month: "2-digit", day: "2-digit" });

/** Día de Colombia como «2026-10-09» (lo que esperan los formularios de la API y los <input type="date">). */
export function diaColombia(instante: Date | string = new Date()): string {
  return ISO_DIA.format(typeof instante === "string" ? new Date(instante) : instante);
}
