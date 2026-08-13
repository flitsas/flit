'use client';

import { TramitesTable } from './TramitesTable';

/**
 * M0 — Entrada por MODALIDAD (desligada de Parametrización). La vista es el
 * listado de trámites (TramitesTable), que ya hospeda —siguiendo el diseño— la tira
 * de KPIs por estado, los tabs por tipo y la búsqueda dentro de los filtros.
 *
 * Flujo del diseño: "Nuevo trámite" entra DIRECTO al asistente (`/tramites/nuevo`) y el tipo se
 * elige dentro del paso 1. El listado ya no decide la modalidad.
 */
interface OperacionViewProps {
  onNewTramite: () => void;
}

export function OperacionView({ onNewTramite }: OperacionViewProps) {
  return (
    <div className="flex min-w-0 flex-col gap-4">
      <TramitesTable onNewTramite={onNewTramite} />
    </div>
  );
}
