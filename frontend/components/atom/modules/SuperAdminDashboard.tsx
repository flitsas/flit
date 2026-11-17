"use client";

import { Dashboard } from "./Dashboard";

/**
 * Inicio del SuperAdmin. Rediseño de los 3 dashboards, F0: por ahora delega en el `Dashboard` de siempre (que ya
 * reconoce al SuperAdmin por el JWT y le pinta el selector de compañía); la sesión S3 lo reescribe con las franjas del
 * prototipo (KPIs del ecosistema, rendimiento por organismo, distribución de operaciones y resumen de compañías).
 */
export function SuperAdminDashboard({ onNewTramite }: { onNewTramite: () => void }) {
  return <Dashboard onNewTramite={onNewTramite} />;
}
