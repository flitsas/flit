// Formato de liquidaciones (spec-visual §0.1; contrato impuestos.v1.yaml: x-flit-literales.pantalla.montoSinDato).
// Un monto `null` es «Sin dato» y nunca 0, «$ 0» ni guion; un 0 es un dato real. Fechas en hora de Colombia.

/** x-flit-literales.pantalla.montoSinDato. */
export const SIN_DATO = "Sin dato";

const MONTO = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", minimumFractionDigits: 0, maximumFractionDigits: 2 });
const CIFRA = new Intl.NumberFormat("es-CO");
const FECHA = new Intl.DateTimeFormat("es-CO", {
  timeZone: "America/Bogota",
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
});
const HORA = new Intl.DateTimeFormat("es-CO", { timeZone: "America/Bogota", hour: "2-digit", minute: "2-digit", hourCycle: "h23" });

const esNumero = (v: unknown): v is number => typeof v === "number" && Number.isFinite(v);

/** ¿El monto es «sin dato»? (null, undefined o no numérico). */
export function esSinDato(valor: number | null | undefined): boolean {
  return !esNumero(valor);
}

/**
 * Monto en pesos colombianos. `null` → «Sin dato»; `0` → «$ 0».
 * @example formatoMonto(1234567) // "$ 1.234.567" (con espacio duro tras el símbolo)
 */
export function formatoMonto(valor: number | null | undefined): string {
  return esNumero(valor) ? MONTO.format(valor) : SIN_DATO;
}

export function formatoCifra(valor: number): string {
  return CIFRA.format(valor);
}

/** dd/mm/aaaa hh:mm en America/Bogota; null o inválida → «Sin dato». */
export function formatoFecha(iso: string | null | undefined): string {
  if (!iso) return SIN_DATO;
  const fecha = new Date(iso);
  if (Number.isNaN(fecha.getTime())) return SIN_DATO;
  const p = Object.fromEntries(FECHA.formatToParts(fecha).map((x) => [x.type, x.value]));
  return `${p.day}/${p.month}/${p.year} ${p.hour}:${p.minute}`;
}

/** hh:mm en America/Bogota (para «Datos desactualizados desde hh:mm»). */
export function formatoHora(fecha: Date): string {
  return HORA.format(fecha);
}

/** Fecha de calendario `aaaa-mm-dd` → dd/mm/aaaa, sin pasar por zona horaria (no se corre de día). */
export function formatoFechaDia(valor: string | null | undefined): string {
  const m = valor ? /^(\d{4})-(\d{2})-(\d{2})/.exec(valor) : null;
  return m ? `${m[3]}/${m[2]}/${m[1]}` : SIN_DATO;
}

/** `{tipo} {número}` sin máscara (DA-7=b); sin número → «Sin dato». */
export function formatoDocumento(tipo: string | null | undefined, numero: string | null | undefined): string {
  if (!numero || !numero.trim()) return SIN_DATO;
  return tipo ? `${tipo} ${numero.trim()}` : numero.trim();
}

/**
 * Secretaría de tránsito (Item.secretaria, contrato 1.1.0): «{nombre}, {ciudad}»; con uno solo, ese (hoy el backend no
 * guarda el nombre y llega null). Sin ninguno, null: la lista pinta «Sin dato».
 * @example formatoSecretaria({ nombre: null, ciudad: "Ciudad Demo" }) // "Ciudad Demo"
 */
export function formatoSecretaria(secretaria: { nombre: string | null; ciudad: string | null } | null | undefined): string | null {
  const partes = [secretaria?.nombre, secretaria?.ciudad].map((v) => v?.trim()).filter((v): v is string => Boolean(v));
  return partes.length > 0 ? partes.join(", ") : null;
}

/** Placa como la normaliza el servidor: mayúsculas, sin espacios ni guiones (" abc-123 " → "ABC123"). */
export function normalizarPlaca(texto: string): string {
  return texto.toUpperCase().replace(/[\s-]+/g, "");
}

/** Regla del contrato (PLACA_INVALIDA): de 4 a 10 letras o números, ya normalizada. */
export function placaValida(placa: string): boolean {
  return /^[A-Z0-9]{4,10}$/.test(placa);
}

/** Primeros 8 caracteres del id en mayúsculas (el id completo va en el title). */
export function idCorto(id: string): string {
  return id.slice(0, 8).toUpperCase();
}
