/**
 * Enlace al detalle del trámite. Para el SuperAdmin lleva el tenant del REGISTRO en `?t=`, que el detalle
 * traduce a la cabecera `X-Tenant-Id`: las pantallas que listan datos de cualquier compañía (LOG QX,
 * Validaciones) no hallaban el trámite en el tenant propio. El resto de usuarios usa el de su sesión: sin `?t=`.
 */
export function hrefTramite(instanceId: string, tenantId: string | null | undefined, esAdmin: boolean): string {
  return esAdmin && tenantId ? `/tramites/${instanceId}?t=${encodeURIComponent(tenantId)}` : `/tramites/${instanceId}`;
}
