// Cliente tipado de las consultas de SuperAdmin sobre TODAS las compañías a la vez.
//
// Es el mismo modelo y el mismo tipo de fila que `./company-queries` — reutiliza `CompanyQueryRow`
// entero — solo que sin un tenant único: operaciones arma "aprobados Tesla", la clona para Renting,
// o pregunta sin filtro y ve las de todas. El backend exige acotar por «Compañía» o por fecha
// cuando no hay tenant que limite el universo por sí solo; ver `SuperAdminQueryTooBroadException`.
import { apiFetch } from "./client";
import type { CompanyQueryRow } from "./company-queries";
import { ApiError } from "./types";
import type { QueryDefinition, QueryField, QueryResult, SaveQueryInput, SavedQuery } from "./queries";
import { QUERY_PAGE_SIZE } from "./queries";

const base = "/api/v1/analytics/superadmin-queries";

export type SuperAdminQueryResult = QueryResult<CompanyQueryRow>;

/** Código que devuelve el backend cuando la consulta no acota ni por compañía ni por fecha. */
export const SUPERADMIN_QUERY_TOO_BROAD_CODE = "CONSULTA_SIN_ACOTAR";

/**
 * El mensaje explicativo del servidor va en el cuerpo (`{ error, code }`), no en `Error.message`
 * —`apiFetch` deja ahí un texto genérico—, así que se reemplaza aquí mismo: `QueryConsole` solo lee
 * `e.message` al fallar una consulta, y así lo hace sin necesidad de conocer este código de error.
 */
function withFriendlyMessage<T>(promise: Promise<T>): Promise<T> {
  return promise.catch((error: unknown) => {
    if (error instanceof ApiError) {
      const body = error.body as { error?: string; code?: string } | null;
      if (body?.code === SUPERADMIN_QUERY_TOO_BROAD_CODE && body.error) {
        throw new Error(body.error);
      }
    }
    throw error;
  });
}

export function fetchSuperAdminQueryFields(signal?: AbortSignal): Promise<QueryField[]> {
  return apiFetch<QueryField[]>(`${base}/fields`, { signal });
}

export function runSuperAdminQuery(
  definition: QueryDefinition,
  options: { page?: number; pageSize?: number; signal?: AbortSignal } = {},
): Promise<SuperAdminQueryResult> {
  return withFriendlyMessage(
    apiFetch<SuperAdminQueryResult>(`${base}/run`, {
      method: "POST",
      body: {
        definition,
        page: options.page ?? 1,
        pageSize: options.pageSize ?? QUERY_PAGE_SIZE,
      },
      signal: options.signal,
    }),
  );
}

export function fetchSuperAdminSavedQueries(signal?: AbortSignal): Promise<SavedQuery[]> {
  return apiFetch<SavedQuery[]>(`${base}/saved`, { signal });
}

export function saveSuperAdminQuery(input: SaveQueryInput): Promise<SavedQuery> {
  return apiFetch<SavedQuery>(`${base}/saved`, { method: "POST", body: input });
}

export function deleteSuperAdminSavedQuery(id: string): Promise<void> {
  return apiFetch<void>(`${base}/saved/${id}`, { method: "DELETE" });
}
