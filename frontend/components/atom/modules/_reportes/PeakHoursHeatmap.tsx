"use client";

// Heatmap de horas pico (Reportes 2.0, HU-C · pestaña Uso del aplicativo).
// Grid 7×24 (día de la semana × hora America/Bogota) con intensidad de color
// proporcional a los eventos. dayOfWeek: 0=domingo … 6=sábado (§4.4).
import type { PeakHour } from "@/lib/api/analytics-v2";
import { formatInt } from "./format";

const DAY_LABELS = ["Dom", "Lun", "Mar", "Mié", "Jue", "Vie", "Sáb"];

export function PeakHoursHeatmap({ data }: { data: PeakHour[] }) {
  const byCell = new Map<string, number>();
  let max = 0;
  for (const p of data) {
    const key = `${p.dayOfWeek}:${p.hour}`;
    const total = (byCell.get(key) ?? 0) + p.events;
    byCell.set(key, total);
    if (total > max) max = total;
  }

  if (max === 0) {
    return <p className="text-xs opacity-60 py-2">Sin actividad registrada en el periodo.</p>;
  }

  return (
    <div className="overflow-x-auto" data-testid="peak-hours-heatmap">
      <div
        className="grid gap-[3px] min-w-[560px]"
        style={{ gridTemplateColumns: "36px repeat(24, minmax(0, 1fr))" }}
        role="img"
        aria-label="Mapa de calor de actividad por día de la semana y hora (hora de Bogotá)"
      >
        {/* Encabezado de horas (cada 3 h para no saturar) */}
        <div />
        {Array.from({ length: 24 }).map((_, h) => (
          <div key={`h-${h}`} className="text-[9px] opacity-60 text-center">
            {h % 3 === 0 ? `${h}h` : ""}
          </div>
        ))}

        {DAY_LABELS.map((label, day) => (
          <Row key={label} day={day} label={label} byCell={byCell} max={max} />
        ))}
      </div>
      <p className="text-[10px] opacity-60 mt-2">
        Intensidad = eventos registrados en esa hora (zona horaria de Bogotá). Máximo: {formatInt(max)} eventos.
      </p>
    </div>
  );
}

function Row({
  day,
  label,
  byCell,
  max,
}: {
  day: number;
  label: string;
  byCell: Map<string, number>;
  max: number;
}) {
  return (
    <>
      <div className="text-[10px] opacity-70 font-medium flex items-center">{label}</div>
      {Array.from({ length: 24 }).map((_, hour) => {
        const events = byCell.get(`${day}:${hour}`) ?? 0;
        const alpha = events === 0 ? 0.06 : 0.15 + 0.85 * (events / max);
        return (
          <div
            key={hour}
            className="aspect-square rounded-[3px] min-w-[10px]"
            style={{ background: `rgba(85, 126, 255, ${alpha.toFixed(2)})` }}
            title={`${label} ${hour}:00 — ${formatInt(events)} eventos`}
          />
        );
      })}
    </>
  );
}
