/**
 * Navegación del módulo "Generación de improntas" (HU #10470, Feature #10462).
 * Solo dos vistas planas (Formulario / Historial) — no se usa un hub layout tipo
 * OtHubLayout (pensado para sub-recursos por organismo de tránsito con `[id]`); en su
 * lugar, una barra de pestañas local mínima (`ImprontasTabs.tsx`) alterna entre las
 * dos rutas del módulo, mismo patrón visual que `OtTabBar` (transit-offices) pero sin
 * acoplar el módulo Improntas a ese feature.
 */
export type ImprontasTabId = "formulario" | "historial";

export interface ImprontasTab {
  id: ImprontasTabId;
  label: string;
}

export const IMPRONTAS_TABS: ImprontasTab[] = [
  { id: "formulario", label: "Generar impronta" },
  { id: "historial", label: "Historial" },
];

export function improntasTabPath(tab: ImprontasTabId): string {
  return tab === "formulario" ? "/admin/improntas" : `/admin/improntas/${tab}`;
}

/** Convierte un `<input type="date">` (YYYY-MM-DD) al límite inferior del día en UTC. */
export function improntaDateFromToIso(value: string): string | undefined {
  return value ? `${value}T00:00:00.000Z` : undefined;
}

/** Convierte un `<input type="date">` (YYYY-MM-DD) al límite superior del día en UTC. */
export function improntaDateToToIso(value: string): string | undefined {
  return value ? `${value}T23:59:59.999Z` : undefined;
}

/** Formatea `fechaImpresa`/`createdAt` (ISO) para mostrar en la tabla de historial. */
export function formatImprontaHistorialDate(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) {
    return iso;
  }
  return parsed.toLocaleString("es-CO", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}
