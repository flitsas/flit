const COMPANIES = "/api/v1/admin/companies";

/**
 * Ruta de recurso de compañía. Con `networkHeadId` usa las APIs de red
 * (`/{cabeza}/children/{hija}…`) para que la cabeza gestione al asociado.
 */
export function companyScopedPath(
  tenantId: string,
  suffix: string,
  networkHeadId?: string | null,
): string {
  return networkHeadId
    ? `${COMPANIES}/${networkHeadId}/children/${tenantId}${suffix}`
    : `${COMPANIES}/${tenantId}${suffix}`;
}
