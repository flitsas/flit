"use client";

// Tarjeta KPI de Reportes 2.0 (HU-C): valor grande, etiqueta, variación frente al
// periodo comparado (flecha + color) y tooltip "cómo se calcula" (atributo title).
import { ArrowDownRight, ArrowUpRight } from "lucide-react";
import { TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
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
  /** Ya no se aplica (HU #14476): la cifra va en el texto principal, como en el prototipo. Se conserva por API. */
  color?: string;
  /** Ícono del kit a la derecha: clave de MAPA_ICONO (`@flit/ui/iconos-mapas`). */
  icono?: string;
  /** `true` cuando subir es MALO (p. ej. % de rechazo): invierte el color de la flecha. */
  invertVariationColor?: boolean;
  testId?: string;
}

const GOOD = "var(--flit-success-ink)";
const BAD = "var(--destructive)";

export function KpiCard({
  label,
  value,
  tooltip,
  variation,
  invertVariationColor = false,
  icono,
  testId,
}: KpiCardProps) {
  // HU #14476 — la tarjeta de cifra del prototipo «Diseño Inteligente» (`TarjetaMetrica`): la cifra va en el texto
  // principal (la paleta de la propuesta no tiñe cifras de verde ni de rojo), así que `color` ya no se aplica;
  // la variación sigue en el pie con su flecha.
  return (
    <TarjetaMetrica
      etiqueta={label}
      cifra={value}
      icono={icono}
      title={tooltip}
      data-testid={testId}
      className="min-h-[96px]"
      pie={
        variation === undefined ? undefined : (
          <span className="font-semibold" aria-live="polite">
            {variation === null ? (
              <span className="opacity-70 font-normal">Sin base de comparación</span>
            ) : (
              <Variation value={variation} invert={invertVariationColor} />
            )}
          </span>
        )
      }
    />
  );
}

function Variation({ value, invert }: { value: number; invert: boolean }) {
  const up = value >= 0;
  const good = invert ? !up : up;
  const colorHex = value === 0 ? "var(--muted-foreground)" : good ? GOOD : BAD;
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
