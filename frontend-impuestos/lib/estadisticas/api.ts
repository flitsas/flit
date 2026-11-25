// Dashboard de Impuestos (impuestos.v1.yaml 1.0.0, tag Dashboard): GET /dashboard?desde&hasta[&empresa]. El Super Admin
// sin `empresa` recibe la vista global con `ranking`; con `empresa`, el de esa compañía. Permiso impuestos.dashboard.ver.
import { ApiError, apiFetch } from "@/lib/api";
import { ERRORES_EMPRESA } from "@/lib/errores";
import type { DashboardImpuestos, Rango } from "./tipos";

export const RUTA_DASHBOARD = "/dashboard";

/** x-flit-literales.errores de la ruta del dashboard. */
export const ERRORES_DASHBOARD: Readonly<Record<string, string>> = {
  RANGO_INVALIDO: "El rango de fechas no es válido.",
  ...ERRORES_EMPRESA,
  PRODUCTO_NO_HABILITADO: "Impuestos no está activo para tu compañía.",
  DEMASIADAS_SOLICITUDES: "Hiciste demasiadas solicitudes. Espera un momento e intenta de nuevo.",
};

export function obtenerDashboard(rango: Rango, empresa: string | null, signal?: AbortSignal): Promise<DashboardImpuestos> {
  const params = new URLSearchParams({ desde: rango.desde, hasta: rango.hasta });
  return apiFetch<DashboardImpuestos>(`${RUTA_DASHBOARD}?${params.toString()}`, { signal, empresa });
}

/** ¿El error es del rango (400 RANGO_INVALIDO)? Se pinta junto a «Hasta», no como error total. */
export function esErrorDeRango(error: unknown): boolean {
  return error instanceof ApiError && error.status === 400 && error.code === "RANGO_INVALIDO";
}
