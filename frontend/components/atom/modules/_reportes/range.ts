// Utilidades de rango de fechas para el filtro del dashboard analítico (HU #10247, AC2).

export interface DateRange {
  from: string;
  to: string;
}

/** Formatea una fecha a `YYYY-MM-DD` en hora local (lo que esperan los inputs `type=date`). */
export function toIsoDate(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

/**
 * Rango por defecto: desde el primer día del mes en curso hasta hoy. `reference` se
 * inyecta en tests para no depender del reloj del sistema.
 */
export function defaultRange(reference: Date = new Date()): DateRange {
  const first = new Date(reference.getFullYear(), reference.getMonth(), 1);
  return { from: toIsoDate(first), to: toIsoDate(reference) };
}

/** `true` si el rango es coherente (inicio no posterior al fin). Espeja la validación del backend (400). */
export function isValidRange(range: DateRange): boolean {
  return Boolean(range.from) && Boolean(range.to) && range.from <= range.to;
}

/**
 * `true` si el rango cabe en `maxMonths` meses calendario (inclusive).
 * HU #11115 AC6 — UI bloquea > 12 meses antes de llamar al backend.
 */
export function isWithinMaxMonths(range: DateRange, maxMonths = 12): boolean {
  if (!isValidRange(range)) return false;
  const start = new Date(`${range.from}T00:00:00`);
  const end = new Date(`${range.to}T00:00:00`);
  const limit = new Date(start);
  limit.setMonth(limit.getMonth() + maxMonths);
  return end <= limit;
}
