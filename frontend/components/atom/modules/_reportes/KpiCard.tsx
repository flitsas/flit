"use client";

// Tarjeta KPI de Reportes 2.0 (HU-C): valor grande, etiqueta, variación frente al
// periodo comparado (flecha + color) y tooltip "cómo se calcula" (atributo title).
import { ArrowDownRight, ArrowUpRight } from "lucide-react";
import { formatNumber } from "./format";

export interface KpiCardProps {
  label: string;
  /** Valor ya formateado (número, porcentaje, horas…). */
  value: string;
  /** Explicación de cómo se calcula la métrica (tooltip nativo). */
  tooltip: string;
  /**
   * Variación % vs el periodo comparado (calculada con `variationPct`).
   * `null` = sin base de comparación; `undefined` = comparación no solicitada.
   */
  variation?: number | null;
  /** Color de acento del valor. */
  color?: string;
  /** `true` cuando subir es MALO (p. ej. % de rechazo): invierte el color de la flecha. */
  invertVariationColor?: boolean;
  testId?: string;
}

const GOOD = "#8CC63F";
const BAD = "#FF4E00";

export function KpiCard({
  label,
  value,
  tooltip,
  variation,
  color = "#162744",
  invertVariationColor = false,
  testId,
}: KpiCardProps) {
  return (
    <div
      className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border flex flex-col justify-between min-h-[96px]"
      title={tooltip}
      data-testid={testId}
    >
      <p className="text-[11px] opacity-70 font-medium">{label}</p>
      <p className="text-2xl font-bold mt-1 dark:text-white" style={{ color }}>
        {value}
      </p>
      {variation !== undefined && (
        <p className="mt-0.5 text-[11px] font-semibold" aria-live="polite">
          {variation === null ? (
            <span className="opacity-50 font-normal">Sin base de comparación</span>
          ) : (
            <Variation value={variation} invert={invertVariationColor} />
          )}
        </p>
      )}
    </div>
  );
}

function Variation({ value, invert }: { value: number; invert: boolean }) {
  const up = value >= 0;
  const good = invert ? !up : up;
  const colorHex = value === 0 ? "#59677D" : good ? GOOD : BAD;
  const Icon = up ? ArrowUpRight : ArrowDownRight;
  return (
    <span
      className="inline-flex items-center gap-0.5"
      style={{ color: colorHex }}
      aria-label={`Variación de ${formatNumber(value)}% frente al periodo comparado`}
    >
      <Icon className="h-3 w-3" aria-hidden="true" />
      {formatNumber(Math.abs(value))}%
      <span className="opacity-60 font-normal ml-0.5">vs comparado</span>
    </span>
  );
}
