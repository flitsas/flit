import type { ListInstancesParams } from '@/lib/api/types/procedure-runtime';

/** Día local `YYYY-MM-DD` → inicio UTC del día (filtro `*From`). */
export function dayStartIso(dateYmd: string): string {
  return `${dateYmd}T00:00:00.000Z`;
}

/** Día local `YYYY-MM-DD` → fin UTC del día (filtro `*To`). */
export function dayEndIso(dateYmd: string): string {
  return `${dateYmd}T23:59:59.999Z`;
}

/**
 * Arma el query string de GET /instances a partir de los filtros/orden.
 * Omite claves vacías; las fechas `YYYY-MM-DD` se expanden a inicio/fin de día ISO.
 */
export function buildListInstancesSearchParams(
  params: Omit<ListInstancesParams, 'filterTenantId'> = {},
): URLSearchParams {
  const q = new URLSearchParams();
  const set = (key: string, value: string | number | boolean | undefined) => {
    if (value === undefined || value === '') return;
    q.set(key, String(value));
  };

  set('vin', params.vin?.trim());
  set('placa', params.placa?.trim());
  set('vendedor', params.vendedor?.trim());
  set('comprador', params.comprador?.trim());
  set('gestor', params.gestor?.trim());
  set('estado', params.estado?.trim());
  set('modalidad', params.modalidad?.trim());
  set('organismoTransito', params.organismoTransito?.trim());
  set('tipoCodigo', params.tipoCodigo?.trim());
  if (params.firmado !== undefined) set('firmado', params.firmado);
  if (params.createdFrom?.trim()) set('createdFrom', dayStartIso(params.createdFrom.trim()));
  if (params.createdTo?.trim()) set('createdTo', dayEndIso(params.createdTo.trim()));
  if (params.updatedFrom?.trim()) set('updatedFrom', dayStartIso(params.updatedFrom.trim()));
  if (params.updatedTo?.trim()) set('updatedTo', dayEndIso(params.updatedTo.trim()));
  set('sortBy', params.sortBy?.trim());
  set('sortDir', params.sortDir);
  if (params.skip !== undefined) set('skip', params.skip);
  if (params.take !== undefined) set('take', params.take);
  return q;
}

/** True si hay algún criterio que active el camino filtrado/ordenado del backend. */
export function hasListInstancesServerQuery(
  params: Omit<ListInstancesParams, 'filterTenantId'> = {},
): boolean {
  return buildListInstancesSearchParams(params).toString().length > 0;
}
