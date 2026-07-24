"use client";

import { DetailedReportPanel } from "./_reportesDetallados/DetailedReportPanel";
import { ModuleTitle } from "./ModuleTitle";

export function ReportesDetallados() {
  return (
    <div className="flex flex-col gap-4">
      <ModuleTitle title="Reportes Detallados" subtitle="Segmentación por persona, transformación, leasing y OT." />
      <DetailedReportPanel />
    </div>
  );
}
