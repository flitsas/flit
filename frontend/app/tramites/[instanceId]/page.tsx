'use client';

import { useParams, useRouter } from 'next/navigation';
import { TramiteWizard } from '@/components/operacion/TramiteWizard';

/**
 * Track B — /tramites/[instanceId]: wizard sobre un draft ya creado. F5 reabre
 * el MISMO trámite (no crea otro): el id viene de la URL, no de un create. El
 * layout activa el modo inmersivo para esta ruta (oculta título + tab).
 */
export default function TramiteInstancePage() {
  const params = useParams<{ instanceId: string }>();
  const router = useRouter();

  return (
    <TramiteWizard
      existingInstanceId={params.instanceId}
      onExit={() => router.push('/tramites')}
    />
  );
}
