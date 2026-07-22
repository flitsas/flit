'use client';

import { useState } from 'react';
import { DetailedReportPanel } from '@/components/atom/modules/_reportesDetallados/DetailedReportPanel';
import { usePermissions } from '@/hooks/usePermissions';
import { TramitesTable } from './TramitesTable';
import type { WizardModalidad } from '@/lib/api/types/procedure-runtime';

/**
 * M0 — Entrada por MODALIDAD (desligada de Parametrización). La vista es el
 * listado de trámites (TramitesTable), que ya hospeda —siguiendo el diseño— el
 * funnel de estados, los botones de registro por modalidad (bajo el funnel) y la
 * búsqueda desplegable. Track B: al elegir modalidad, delega en la ruta
 * (onStartTramite → /tramites/nuevo/[modalidad]) la creación + navegación.
 * RES-10 (Feature #10813): consolidado de reporte detallado embebido.
 */
interface OperacionViewProps {
  onStartTramite: (modalidad: WizardModalidad) => void;
}

export function OperacionView({ onStartTramite }: OperacionViewProps) {
  const { permissions, isSuperAdmin } = usePermissions();
  const canDetailedReport = isSuperAdmin || permissions.includes('reportes.detallados.read');
  const [showConsolidado, setShowConsolidado] = useState(false);

  return (
    <div className="flex min-w-0 flex-col gap-4">
      <TramitesTable onStartTramite={onStartTramite} />
      {canDetailedReport && (
        <section className="rounded-2xl border p-4 bg-white dark:bg-[#0B0F14]">
          <div className="flex items-center justify-between gap-3 mb-3">
            <div>
              <h3 className="text-sm font-bold text-[#162744] dark:text-white">Consolidado detallado (RES-10)</h3>
              <p className="text-xs opacity-70">Mismos filtros y descarga que el módulo Reportes Detallados.</p>
            </div>
            <button
              type="button"
              className="rounded-xl border px-3 py-2 text-xs font-semibold"
              onClick={() => setShowConsolidado((v) => !v)}
            >
              {showConsolidado ? 'Ocultar consolidado' : 'Ver consolidado'}
            </button>
          </div>
          {showConsolidado && <DetailedReportPanel embedded />}
        </section>
      )}
    </div>
  );
}
