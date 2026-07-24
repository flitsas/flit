// Helpers de presentación de las escrituras (HU #10905): formateo de fechas, estado de vigencia con
// tono semántico para el StatusBadge y resolución de nombres de compañías a partir de sus ids.
import type { StatusTone } from "@/components/atom/StatusBadge";
import type { RepresentedCompany } from "@/lib/api/admin-deeds";

/** Formatea una fecha YYYY-MM-DD (o ISO) a dd/mm/aaaa en es-CO, robusto ante valores vacíos. */
export function formatDate(value: string | null | undefined): string {
  if (!value) return "—";
  // Fechas puras (YYYY-MM-DD) se parsean como UTC para evitar corrimientos por zona horaria.
  const iso = /^\d{4}-\d{2}-\d{2}$/.test(value) ? `${value}T00:00:00Z` : value;
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return value;
  return d.toLocaleDateString("es-CO", { day: "2-digit", month: "2-digit", year: "numeric", timeZone: "UTC" });
}

/** Fecha de hoy en formato YYYY-MM-DD (UTC), comparable lexicográficamente con las vigencias. */
function todayYmd(): string {
  return new Date().toISOString().slice(0, 10);
}

/** Días completos entre dos fechas YYYY-MM-DD (b − a), 0 si alguna es inválida. */
function daysBetween(a: string, b: string): number {
  const start = Date.parse(`${a}T00:00:00Z`);
  const end = Date.parse(`${b}T00:00:00Z`);
  if (Number.isNaN(start) || Number.isNaN(end)) return 0;
  return Math.round((end - start) / 86_400_000);
}

/**
 * Estado de vigencia de una escritura para el StatusBadge: programada (aún no inicia), vencida (ya
 * pasó) o vigente (con los días restantes hasta el fin de vigencia).
 */
export function vigenciaStatus(
  vigenciaDesde: string,
  vigenciaHasta: string,
): { tone: StatusTone; label: string } {
  const today = todayYmd();
  if (today < vigenciaDesde) return { tone: "info", label: "Programada" };
  if (today > vigenciaHasta) return { tone: "danger", label: "Vencida" };
  const days = daysBetween(today, vigenciaHasta);
  return {
    tone: "success",
    label: days === 0 ? "Vigente (último día)" : `Vigente (${days} día${days === 1 ? "" : "s"})`,
  };
}

/** Nombres de las compañías cuyos ids aparecen en la escritura (según el catálogo derivado). */
export function companyNames(ids: readonly string[], companies: readonly RepresentedCompany[]): string[] {
  return ids.map((id) => companies.find((c) => c.id === id)?.name ?? "Compañía");
}
