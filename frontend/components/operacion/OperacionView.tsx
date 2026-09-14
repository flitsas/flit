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
  /** HU #12521 — abre el modal de carga masiva por Excel. */
  onBulkUpload?: () => void;
  /** Fuerza el recargue del listado (p. ej. tras encolar un lote). */
  refreshKey?: number;
}

export function OperacionView({ onNewTramite, onBulkUpload, refreshKey }: OperacionViewProps) {
  return (
    <div className="flex min-w-0 flex-col gap-4">
      <TramitesTable refreshKey={refreshKey} onNewTramite={onNewTramite} onBulkUpload={onBulkUpload} />
    </div>
  );
}
