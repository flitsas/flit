// Inicio del Gestor (rediseño de los 3 dashboards): la cola de gestión, que el overview no trae. «Trámites por firma»
// sale del atajo `sin_firmas` del listado de trámites (tramitesClient), no de aquí.
// Backend: `Flit.Api/Endpoints/Analytics/GestorDashboardEndpoints.cs`. Acotadas al tenant del JWT; Ver por familia en
// el servidor (una familia sin Ver no cuenta ni se lista).
import { apiFetch } from "./client";

const base = "/api/v1/analytics/gestor";

/** Categoría de familia del contrato del front (la misma del overview). */
export type CategoriaGestor = "matriculas" | "traspasos" | "otros";

export interface ColaInactividadItem {
  id: string;
  referencia: string | null;
  placa: string | null;
  vin: string | null;
  estado: string;
  familia: CategoriaGestor;
  diasSinMovimiento: number;
}

export interface ColaInactividad {
  items: ColaInactividadItem[];
  /** Cuántos cumplen el filtro (no solo los que vienen en `items`). */
  total: number;
}

/** GET /cola-inactividad — sin estado, el servidor deja fuera los finales (aprobado, anulado, revocado). */
export function fetchColaInactividad(
  params: { estado?: string; familia?: CategoriaGestor; limit?: number },
  signal?: AbortSignal,
): Promise<ColaInactividad> {
  return apiFetch<ColaInactividad>(`${base}/cola-inactividad`, {
    query: { estado: params.estado || undefined, familia: params.familia, limit: params.limit },
    signal,
  });
}
