"use client";

// Embudo de estados N03 (Reportes 2.0, HU-C · pestaña Operación/Trámites).
// Barras horizontales con Recharts + % de conversión por etapa; cada etapa es
// clicable para hacer drill-down al detalle de trámites de ese estado.
import { Bar, BarChart, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import type { FunnelStage } from "@/lib/api/analytics-v2";
import { statusColor, statusLabel } from "./categories";
import { formatInt, formatPct } from "./format";

export interface FunnelChartProps {
  states: FunnelStage[];
  /** Etapas del periodo comparado, si las hay. */
  previousStates?: FunnelStage[] | null;
  onSelectStage?: (stage: string) => void;
}

export function FunnelChart({ states, previousStates, onSelectStage }: FunnelChartProps) {
  if (states.length === 0) {
    return <p className="text-xs opacity-60 py-2">Sin trámites en el periodo para construir el embudo.</p>;
  }
  const prevByStage = new Map((previousStates ?? []).map((s) => [s.stage, s.count]));

  return (
    <div className="flex flex-col gap-3">
      <div className="h-48" role="img" aria-label={`Embudo de estados: ${states.map((s) => `${statusLabel(s.stage)} ${s.count}`).join(", ")}`}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={[...states]} layout="vertical" margin={{ left: 8, right: 24, top: 4, bottom: 4 }}>
            <XAxis type="number" hide />
            <YAxis
              type="category"
              dataKey="stage"
              width={86}
              tick={{ fontSize: 11 }}
              tickFormatter={(v: string) => statusLabel(v)}
              axisLine={false}
              tickLine={false}
            />
            <Tooltip
              formatter={(value: number) => [formatInt(value), "Trámites"]}
              labelFormatter={(label: string) => statusLabel(label)}
              contentStyle={{ background: "rgba(22,39,68,0.95)", border: "none", borderRadius: 10, color: "#fff", fontSize: 11 }}
            />
            <Bar
              dataKey="count"
              radius={[0, 6, 6, 0]}
              isAnimationActive={false}
              onClick={onSelectStage ? (entry: FunnelStage) => onSelectStage(entry.stage) : undefined}
              className={onSelectStage ? "cursor-pointer" : undefined}
            >
              {states.map((s, i) => (
                <Cell key={s.stage} fill={statusColor(s.stage, i)} />
              ))}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>

      {/* Detalle textual accesible + drill-down por etapa */}
      <ul className="flex flex-col gap-1">
        {states.map((s, i) => {
          const prev = prevByStage.get(s.stage);
          const content = (
            <span className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-[11px]">
              <span className="h-2 w-2 rounded-full shrink-0" style={{ background: statusColor(s.stage, i) }} />
              <span className="font-semibold">{statusLabel(s.stage)}</span>
              <span className="font-bold">{formatInt(s.count)}</span>
              <span className="opacity-70">
                {formatPct(s.pctOfFirst)} del total · {formatPct(s.pctOfPrev)} conversión desde la etapa anterior
              </span>
              {prev !== undefined && <span className="opacity-50">(comparado: {formatInt(prev)})</span>}
            </span>
          );
          return (
            <li key={s.stage}>
              {onSelectStage ? (
                <button
                  type="button"
                  onClick={() => onSelectStage(s.stage)}
                  className="w-full text-left rounded-lg px-1.5 py-1 hover:bg-[#557EFF14] focus:bg-[#557EFF14] outline-none"
                  aria-label={`Ver trámites en estado ${statusLabel(s.stage)}`}
                >
                  {content}
                </button>
              ) : (
                <div className="px-1.5 py-1">{content}</div>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
