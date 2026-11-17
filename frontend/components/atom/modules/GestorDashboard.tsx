"use client";

import { Dashboard } from "./Dashboard";

/**
 * Inicio del Gestor (usuario de una compañía). Rediseño de los 3 dashboards, F0: por ahora delega en el `Dashboard`
 * de siempre; la sesión S1 lo reescribe con las franjas del prototipo (KPIs por familia, estado general, trámites por
 * firma y cola de gestión). `Dashboard.tsx` queda congelado hasta que S0 lo retire al integrar.
 */
export function GestorDashboard({ onNewTramite }: { onNewTramite: () => void }) {
  return <Dashboard onNewTramite={onNewTramite} />;
}
