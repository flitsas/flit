'use client';

import { TramitesTable } from './TramitesTable';

/**
 * M0 — Entrada por MODALIDAD (desligada de Parametrización). La vista es el
 * listado de trámites (TramitesTable), que hospeda —según flit-tramites-chrome— título,
 * tabs+filtros, KPIs por estado y la tabla.
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
