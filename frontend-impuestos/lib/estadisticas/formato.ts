// Formato es-CO del dashboard y de los reportes (spec-visual §0.1). Un monto null es «Sin dato» (x-flit-literales.pantalla
// .montoSinDato) y nunca $ 0; un 0 es un dato real. Las fechas van en hora de Bogotá.

export const MONTO_SIN_DATO = "Sin dato";

const ENTERO = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 0 });
const MONEDA = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", minimumFractionDigits: 0, maximumFractionDigits: 2 });
const FECHA = new Intl.DateTimeFormat("es-CO", { timeZone: "America/Bogota", day: "2-digit", month: "2-digit", year: "numeric" });
const FECHA_HORA = new Intl.DateTimeFormat("es-CO", {
  timeZone: "America/Bogota",
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  hour12: false,
});

export function formatoEntero(valor: number | null | undefined): string {
  return valor == null || !Number.isFinite(valor) ? "—" : ENTERO.format(valor);
}

/** Monto en pesos; null o no numérico ⇒ «Sin dato». */
export function formatoMonto(valor: number | null | undefined): string {
  return valor == null || !Number.isFinite(valor) ? MONTO_SIN_DATO : MONEDA.format(valor);
}

/** «dd/mm/aaaa» de un instante ISO. */
export function formatoFecha(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : FECHA.format(d);
}

/** «dd/mm/aaaa hh:mm» de un instante ISO. */
export function formatoFechaHora(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  const partes = Object.fromEntries(FECHA_HORA.formatToParts(d).map((p) => [p.type, p.value]));
  return `${partes.day}/${partes.month}/${partes.year} ${partes.hour}:${partes.minute}`;
}

/** Rótulo de un día de la serie (AAAA-MM-DD) en el eje: «24/11». */
export function rotuloDia(fecha: string): string {
  const [, m, d] = fecha.split("-");
  return d && m ? `${d}/${m}` : fecha;
}
