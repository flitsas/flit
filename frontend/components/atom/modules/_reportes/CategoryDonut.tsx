"use client";

import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip } from "recharts";
import type { CategoryMetrics } from "@/lib/api/types";
import { CATEGORY_META, statusColor, statusLabel } from "./categories";

interface CategoryDonutProps {
  metrics: CategoryMetrics;
}

/**
 * Gráfico circular (donut) de una categoría de trámite (HU #10247, AC1/RF02): reparte
 * el total por estado. Accesible: el SVG no es legible por lectores de pantalla, así
 * que se acompaña de un resumen `sr-only` y un `aria-label` con la composición.
 */
export function CategoryDonut({ metrics }: CategoryDonutProps) {
  const meta = CATEGORY_META[metrics.category];
  const slices = metrics.byStatus.filter((s) => s.count > 0);
  const hasData = metrics.total > 0 && slices.length > 0;

  const summary = slices.map((s) => `${statusLabel(s.status)}: ${s.count}`).join(", ");

  return (
    <section
      className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border flex flex-col min-h-[240px]"
      style={{ borderColor: "#DFE5ED" }}
      aria-labelledby={`donut-${metrics.category}-title`}
    >
      <div className="flex items-center justify-between shrink-0">
        <h3 id={`donut-${metrics.category}-title`} className="text-sm font-bold">
          {meta.label}
        </h3>
        <span className="text-lg font-bold" style={{ color: meta.color }}>
          {metrics.total}
        </span>
      </div>

      {hasData ? (
        <>
          <div
            className="flex-1 min-h-0"
            role="img"
            aria-label={`Distribución de ${meta.label} por estado. Total ${metrics.total}. ${summary}.`}
          >
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie
                  data={slices}
                  dataKey="count"
                  nameKey="status"
                  cx="50%"
                  cy="50%"
                  innerRadius="55%"
                  outerRadius="80%"
                  paddingAngle={2}
                  stroke="none"
                  isAnimationActive={false}
                >
                  {slices.map((s, i) => (
                    <Cell key={s.status} fill={statusColor(s.status, i)} />
                  ))}
                </Pie>
                <Tooltip
                  formatter={(val: number, name: string) => [val, statusLabel(name)]}
                  contentStyle={{
                    background: "rgba(22,39,68,0.95)",
                    border: "none",
                    borderRadius: 10,
                    color: "#fff",
                    fontSize: 11,
                  }}
                />
              </PieChart>
            </ResponsiveContainer>
          </div>
          <ul className="flex flex-wrap gap-x-3 gap-y-1 mt-2 shrink-0">
            {slices.map((s, i) => (
              <li key={s.status} className="flex items-center gap-1 text-[10px]">
                <span className="h-2 w-2 rounded-full" style={{ background: statusColor(s.status, i) }} />
                <span className="opacity-75">{statusLabel(s.status)}</span>
                <span className="font-semibold">{s.count}</span>
              </li>
            ))}
          </ul>
        </>
      ) : (
        <div className="flex-1 grid place-items-center text-xs opacity-60" data-testid={`donut-empty-${metrics.category}`}>
          Sin trámites en el periodo
        </div>
      )}
    </section>
  );
}
