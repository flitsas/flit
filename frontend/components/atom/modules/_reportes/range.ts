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
 *
 * OJO — el Dashboard analítico ya NO arranca con esto (BUG #12588): parte {@link sinRango} para que
 * su total sea el universo real y no el del mes en curso. Se conserva porque `IctReports` sí quiere
 * abrir acotado a un periodo, y porque el usuario puede volver a este rango desde el filtro.
 */
export function defaultRange(reference: Date = new Date()): DateRange {
  const first = new Date(reference.getFullYear(), reference.getMonth(), 1);
  return { from: toIsoDate(first), to: toIsoDate(reference) };
}

/**
 * Rango vacío: sin acotar por fecha. BUG #12588 — el backend lo interpreta como «todo el universo»,
 * y es el estado inicial del Dashboard.
 */
export function sinRango(): DateRange {
  return { from: "", to: "" };
}

/** `true` si no hay ningún extremo puesto: la consulta no acota por fecha. */
export function esRangoVacio(range: DateRange): boolean {
  return !range.from && !range.to;
}

/**
 * `true` si el rango es coherente (inicio no posterior al fin). Espeja la validación del backend
 * (400) para los consumidores que EXIGEN rango —`Reportes`, `IctReports`, `DetailedReportPanel`:
 * sus endpoints mantienen `from`/`to` obligatorios—, así que un extremo vacío sigue siendo inválido
 * ahí y el panel bloquea la consulta antes de salir a la red.
 */
export function isValidRange(range: DateRange): boolean {
  return Boolean(range.from) && Boolean(range.to) && range.from <= range.to;
}

/**
 * Variante para el overview analítico, cuyo `from`/`to` son OPCIONALES (BUG #12588): vacío o con un
 * solo extremo es válido —acota por ese lado o por ninguno— y solo se rechaza el par incoherente,
 * que es lo único que el backend responde con 400.
 */
export function isValidOptionalRange(range: DateRange): boolean {
  if (!range.from || !range.to) return true;
  return range.from <= range.to;
}
