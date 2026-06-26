'use client';

import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { TramiteWizard } from '@/components/operacion/TramiteWizard';
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
 */
export default function TramiteInstancePage() {
  const params = useParams<{ instanceId: string }>();
  const searchParams = useSearchParams();
  const router = useRouter();

  setActiveTramitesTenant(searchParams.get('t') ?? undefined);

  return (
    <TramiteWizard
      existingInstanceId={params.instanceId}
      onExit={() => router.push('/tramites')}
    />
  );
}
