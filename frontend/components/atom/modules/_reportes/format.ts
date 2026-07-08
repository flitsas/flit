// Formateo de números y duraciones para las métricas de Reportes 2.0 (HU-C).
// Locale es-CO (separador de miles con punto, decimal con coma).

const numberFmt = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 1 });
const intFmt = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 0 });

/** Número entero legible ("12.450"); "—" si no hay dato. */
export function formatInt(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  return intFmt.format(value);
}

/** Número con hasta 1 decimal; "—" si no hay dato. */
export function formatNumber(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  return numberFmt.format(value);
}

/** Porcentaje con 1 decimal ("16,7%"); "—" si no hay dato. */
export function formatPct(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  return `${numberFmt.format(value)}%`;
}

/** Horas legibles: "52,4 h"; si supera 72 h muestra días ("4,2 días"). */
export function formatHours(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  if (value > 72) return `${numberFmt.format(value / 24)} días`;
  return `${numberFmt.format(value)} h`;
}

/** Duración en ms → texto: "850 ms", "12,5 s", "31 min" o "2,1 h". */
export function formatDurationMs(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  if (value < 1000) return `${intFmt.format(value)} ms`;
  if (value < 60_000) return `${numberFmt.format(value / 1000)} s`;
  if (value < 3_600_000) return `${numberFmt.format(value / 60_000)} min`;
  return `${numberFmt.format(value / 3_600_000)} h`;
}
