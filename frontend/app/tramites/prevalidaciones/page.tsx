import { PrevalidacionesModule } from '@/components/atom/modules/PrevalidacionesModule';

/**
 * Ruta /tramites/prevalidaciones — Pantalla dedicada de prevalidación de identidad.
 * HU #10868 (Feature #10864 — CF-01).
 * El chrome (Shell del layout de /tramites) lo pone TramitesLayout.
 */
export default function PrevalidacionesPage() {
  return <PrevalidacionesModule />;
}
