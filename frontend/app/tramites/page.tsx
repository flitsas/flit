'use client';

import { useRouter } from 'next/navigation';
import { OperacionView } from '@/components/operacion/OperacionView';

/**
 * Track B — /tramites: listado de operación (chooser de modalidad + tabla).
 * Al elegir modalidad navega a /tramites/nuevo/[modalidad], que crea la
 * instancia y redirige al wizard. El chrome (título + tab) lo pone el layout.
 */
export default function TramitesPage() {
  const router = useRouter();
  return (
    <OperacionView
      onStartTramite={(modalidad) => router.push(`/tramites/nuevo/${modalidad}`)}
    />
  );
}
