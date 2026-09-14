'use client';

import { useParams, useSearchParams } from 'next/navigation';
import { TramiteInstanceGate } from '@/components/operacion/TramiteInstanceGate';
import { setActiveTramitesTenant } from '@/lib/api/tramites-client';

/**
 * Track B — /tramites/[instanceId]: wizard sobre un draft ya creado. F5 reabre
 * el MISMO trámite (no crea otro): el id viene de la URL, no de un create. El
 * layout activa el modo inmersivo para esta ruta (oculta título + tab).
 *
 * #1 — Si la URL trae `?t=<tenantId>` (el SuperAdmin abrió un trámite de OTRA compañía desde el
 * listado multi-tenant), se fija ese tenant como activo para que las llamadas per-instance lo
 * lleven en X-Tenant-Id. Para un usuario de compañía no hay `?t=` y el backend deriva su tenant
 * del JWT. Se setea en el render (no en un effect) para que la PRIMERA carga ya use el tenant.
 *
 * Estado del trámite / Anular / Prenda viven DENTRO del contenido scrolleable del wizard
 * (un solo scroll). Los historiales de identidad y de estados no se muestran en esta vista.
 *
 * HU #12362 (AC7) — `TramiteInstanceGate` decide si esta dirección abre el asistente o, para la
 * cabeza de red sobre un trámite de un cliente hijo, el detalle en modo consulta. Para el resto
 * de usuarios es el mismo asistente de siempre.
 */
export default function TramiteInstancePage() {
  const params = useParams<{ instanceId: string }>();
  const searchParams = useSearchParams();

  setActiveTramitesTenant(searchParams.get('t') ?? undefined);

  return <TramiteInstanceGate instanceId={params.instanceId} />;
}
