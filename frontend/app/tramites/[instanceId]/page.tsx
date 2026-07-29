'use client';

import { useState } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { TramiteWizard } from '@/components/operacion/TramiteWizard';
import { EstadoTimelinePanel } from '@/components/operacion/EstadoTimeline';
import { EstadoAcciones } from '@/components/operacion/EstadoAcciones';
import { IdentityStatusPanel } from '@/components/operacion/IdentityStatusPanel';
import { PrendaModificar } from '@/components/operacion/PrendaModificar';
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
 * Seguimiento (identidad + historial de estados) va al final: no interrumpe el wizard ni las
 * acciones de transición. La identidad muestra solo aprobaciones/rechazos en vivo.
 */
export default function TramiteInstancePage() {
  const params = useParams<{ instanceId: string }>();
  const searchParams = useSearchParams();
  const router = useRouter();
  const [refreshKey, setRefreshKey] = useState(0);

  setActiveTramitesTenant(searchParams.get('t') ?? undefined);

  return (
    <>
      <TramiteWizard
        key={`wizard-${refreshKey}`}
        existingInstanceId={params.instanceId}
        onExit={() => router.push('/tramites')}
      />
      <EstadoAcciones
        key={`acciones-${refreshKey}`}
        instanceId={params.instanceId}
        onChanged={() => setRefreshKey((k) => k + 1)}
      />
      {/* R17 (HU #10600) — modificar la elección de prenda post-registro. */}
      <PrendaModificar key={`prenda-${refreshKey}`} instanceId={params.instanceId} />

      {/* Seguimiento al final: historial de identidad + historial de estados (mismo patrón disclosure). */}
      <IdentityStatusPanel key={`identidad-${refreshKey}`} instanceId={params.instanceId} />
      <EstadoTimelinePanel key={`timeline-${refreshKey}`} instanceId={params.instanceId} />
    </>
  );
}
