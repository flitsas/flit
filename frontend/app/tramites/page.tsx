'use client';

import { useRouter } from 'next/navigation';
import { OperacionView } from '@/components/operacion/OperacionView';

/**
 * Track B — /tramites: listado de operación (KPIs + tabs + tabla).
 * "Nuevo trámite" entra directo a /tramites/nuevo, donde se elige el trámite principal;
 * esa página continúa a /tramites/nuevo/[modalidad], que crea la instancia al Continuar.
 * El chrome (título + tab) lo pone el layout.
 */
export default function TramitesPage() {
  const router = useRouter();
  return <OperacionView onNewTramite={() => router.push('/tramites/nuevo')} />;
}
