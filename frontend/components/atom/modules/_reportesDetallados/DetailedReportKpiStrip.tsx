"use client";

import type { DetailedReportSummary } from "@/lib/api/detailed-report";
import { KpiCard } from "../_reportes/KpiCard";
import { formatNumber } from "../_reportes/format";
import { COPY } from "@/lib/copy/copy-catalog";

interface DetailedReportKpiStripProps {
  summary?: DetailedReportSummary | null;
}

export function DetailedReportKpiStrip({ summary }: DetailedReportKpiStripProps) {
  const total = summary?.totalCount ?? 0;
  const topCategory = summary?.byCategory[0];
  const topStatus = summary?.byStatus[0];
  const topType = summary?.byProcedureType[0];

  return (
    <div className="grid grid-cols-[repeat(auto-fit,minmax(min(200px,100%),1fr))] gap-4">
      <KpiCard
        label={COPY.E03}
        icono="tramites"
        value={formatNumber(total)}
        tooltip="Trámites que cumplen los filtros activos."
        testId="detailed-kpi-total"
      />
      <KpiCard
        label="Categoría principal"
        icono="filtros"
        value={topCategory ? `${topCategory.label} (${formatNumber(topCategory.count)})` : "—"}
        tooltip="Categoría con más trámites en el resultado filtrado."
        color="var(--color-flit-brand)"
        testId="detailed-kpi-category"
      />
      <KpiCard
        label="Estado principal"
        icono="trazabilidad"
        value={topStatus ? `${topStatus.status} (${formatNumber(topStatus.count)})` : "—"}
        tooltip="Estado con más trámites en el resultado filtrado."
        color="var(--flit-success-ink)"
        testId="detailed-kpi-status"
      />
      <KpiCard
        label="Tipo de trámite principal"
        icono="documentos"
        value={topType ? `${topType.label} (${formatNumber(topType.count)})` : "—"}
        tooltip="Tipo de trámite con más registros en el resultado filtrado."
        color="var(--color-flit-primary)"
        testId="detailed-kpi-type"
      />
    </div>
  );
}
