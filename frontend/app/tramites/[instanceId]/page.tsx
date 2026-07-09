'use client';

import { useState } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { TramiteWizard } from '@/components/operacion/TramiteWizard';
import { EstadoTimelinePanel } from '@/components/operacion/EstadoTimeline';
import { EstadoAcciones } from '@/components/operacion/EstadoAcciones';
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
 */
export default function TramiteInstancePage() {
  const params = useParams<{ instanceId: string }>();
  const searchParams = useSearchParams();
  const router = useRouter();
  // N 03 — tras una transición de estado se remonta wizard/acciones/timeline (key) para
  // que refresquen su estado server-driven sin recargar la página.
  const [refreshKey, setRefreshKey] = useState(0);

  setActiveTramitesTenant(searchParams.get('t') ?? undefined);

  return (
    <>
      <TramiteWizard
        key={`wizard-${refreshKey}`}
        existingInstanceId={params.instanceId}
        onExit={() => router.push('/tramites')}
      />
      {/* N 03 — estado actual + acciones de transición permitidas por la máquina (backend manda). */}
      <EstadoAcciones
        key={`acciones-${refreshKey}`}
        instanceId={params.instanceId}
        onChanged={() => setRefreshKey((k) => k + 1)}
      />
      {/* R17 (HU #10600) — modificar la elección de prenda post-registro (solo si hay prenda vigente). */}
      <PrendaModificar key={`prenda-${refreshKey}`} instanceId={params.instanceId} />
      {/* HU-2 (N03, RF05) — historial de transiciones bajo el wizard (colapsado por defecto). */}
      <EstadoTimelinePanel key={`timeline-${refreshKey}`} instanceId={params.instanceId} />
    </>
  );
}
