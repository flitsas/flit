"use client";

import { SchedulesSection } from "../scheduling/SchedulesSection";
import { AlertsSection } from "../scheduling/AlertsSection";

/** Tab Programados — HU #11114 (antes modal SchedulingPanel). */
export function ProgramadosTab({ tenantId }: { tenantId?: string }) {
  return (
    <div className="rounded-2xl border bg-white dark:bg-[#0B0F14] p-4" data-testid="tab-programados">
      <h3 className="text-sm font-semibold mb-3">Informes programados</h3>
      <SchedulesSection tenantId={tenantId} />
    </div>
  );
}

/** Tab Alertas — HU #11114. */
export function AlertasTab({ tenantId }: { tenantId?: string }) {
  return (
    <div className="rounded-2xl border bg-white dark:bg-[#0B0F14] p-4" data-testid="tab-alertas">
      <h3 className="text-sm font-semibold mb-3">Alertas</h3>
      <AlertsSection tenantId={tenantId} />
    </div>
  );
}
