"use client";

import type { DetailedReportSummary } from "@/lib/api/detailed-report";
import { KpiCard } from "../_reportes/KpiCard";
import { formatNumber } from "../_reportes/format";

interface DetailedReportKpiStripProps {
  summary?: DetailedReportSummary | null;
}

export function DetailedReportKpiStrip({ summary }: DetailedReportKpiStripProps) {
  const total = summary?.totalCount ?? 0;
  const topCategory = summary?.byCategory[0];
  const topStatus = summary?.byStatus[0];
  const topType = summary?.byProcedureType[0];

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-3">
      <KpiCard
        label="Total trámites"
        value={formatNumber(total)}
        tooltip="Trámites que cumplen los filtros activos."
        testId="detailed-kpi-total"
      />
      <KpiCard
        label="Categoría principal"
        value={topCategory ? `${topCategory.label} (${formatNumber(topCategory.count)})` : "—"}
        tooltip="Categoría con más trámites en el resultado filtrado."
        color="#557EFF"
        testId="detailed-kpi-category"
      />
      <KpiCard
        label="Estado principal"
        value={topStatus ? `${topStatus.status} (${formatNumber(topStatus.count)})` : "—"}
        tooltip="Estado con más trámites en el resultado filtrado."
        color="#8CC63F"
        testId="detailed-kpi-status"
      />
      <KpiCard
        label="Tipo de trámite principal"
        value={topType ? `${topType.label} (${formatNumber(topType.count)})` : "—"}
        tooltip="Tipo de trámite con más registros en el resultado filtrado."
        color="#162744"
        testId="detailed-kpi-type"
      />
    </div>
  );
}
