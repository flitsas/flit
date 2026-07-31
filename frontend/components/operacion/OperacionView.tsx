'use client';

import { TramitesTable } from './TramitesTable';
import type { WizardModalidad } from '@/lib/api/types/procedure-runtime';

/**
 * M0 — Entrada por MODALIDAD (desligada de Parametrización). La vista es el
 * listado de trámites (TramitesTable), que ya hospeda —siguiendo el diseño— el
 * funnel de estados, los botones de registro por modalidad (bajo el funnel) y la
 * búsqueda desplegable. Track B: al elegir modalidad, delega en la ruta
 * (onStartTramite → /tramites/nuevo/[modalidad]) la creación + navegación.
 */
interface OperacionViewProps {
  onStartTramite: (modalidad: WizardModalidad) => void;
}

export function OperacionView({ onStartTramite }: OperacionViewProps) {
  return (
    <div className="flex min-w-0 flex-col gap-4">
      <TramitesTable onStartTramite={onStartTramite} />
    </div>
  );
}
