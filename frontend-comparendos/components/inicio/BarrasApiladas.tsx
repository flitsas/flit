"use client";

import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { EJE_TICK, REJILLA, TOOLTIP_CONTENIDO, TOOLTIP_CURSOR, TOOLTIP_ETIQUETA, type SerieGrafica } from "./graficos-kit";
import { LeyendaGrafica } from "./LeyendaGrafica";

/** Una fila de la gráfica: su rótulo y un valor por cada `clave` de serie. */
export type FilaApilada = { etiqueta: string } & Record<string, number | string>;

export interface BarrasApiladasProps {
  filas: readonly FilaApilada[];
  series: readonly SerieGrafica[];
  /** `horizontal` (barras acostadas, una fila por categoría) o `vertical` (columnas, p. ej. meses). */
  orientacion?: "horizontal" | "vertical";
  /** Barras lado a lado en vez de apiladas (p. ej. un año contra otro). */
  agrupadas?: boolean;
  formatoEje?: (valor: number) => string;
  formato?: (valor: number) => string;
  /** Alto por fila en px (horizontal) o alto total (vertical). */
  altoFila?: number;
  alto?: number;
  /** Ancho del eje de rótulos en horizontal (los nombres de organismo son largos). */
  anchoRotulo?: number;
}

/**
 * Barras del Inicio y de Reportes, copiadas de `frontend/components/inicio/BarrasApiladas.tsx` (rediseño de
 * Comparendos, S2) con formato de pesos y orientación vertical para las series mensuales. La leyenda va debajo y es la
 * lectura accesible de las series; cada fila se resume además en una lista oculta.
 */
export function BarrasApiladas({
  filas,
  series,
  orientacion = "horizontal",
  agrupadas = false,
  formatoEje = (v) => v.toLocaleString("es-CO"),
  formato = (v) => v.toLocaleString("es-CO"),
  altoFila = 40,
  alto,
  anchoRotulo = 132,
}: BarrasApiladasProps) {
  const horizontal = orientacion === "horizontal";
  const altura = alto ?? (horizontal ? Math.max(120, filas.length * altoFila + 32) : 260);
  const recortar = (t: string) => (t.length > 22 ? `${t.slice(0, 21)}…` : t);
  return (
    <div>
      <div className="-mx-2" style={{ height: altura }} aria-hidden="true">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={[...filas]} layout={horizontal ? "vertical" : "horizontal"} margin={{ top: 4, right: 12, left: 0, bottom: 0 }}>
            <CartesianGrid {...REJILLA} horizontal={!horizontal} vertical={false} />
            {horizontal ? (
              <>
                <XAxis type="number" tick={EJE_TICK} axisLine={false} tickLine={false} allowDecimals={false} tickFormatter={formatoEje} />
                <YAxis type="category" dataKey="etiqueta" tick={EJE_TICK} axisLine={false} tickLine={false} width={anchoRotulo} tickFormatter={recortar} />
              </>
            ) : (
              <>
                <XAxis dataKey="etiqueta" tick={EJE_TICK} axisLine={false} tickLine={false} minTickGap={4} />
                <YAxis tick={EJE_TICK} axisLine={false} tickLine={false} allowDecimals={false} tickFormatter={formatoEje} width={64} />
              </>
            )}
            <Tooltip cursor={TOOLTIP_CURSOR} contentStyle={TOOLTIP_CONTENIDO} labelStyle={TOOLTIP_ETIQUETA} formatter={(v: number, nombre: string) => [formato(v), nombre]} />
            {series.map((s, i) => (
              <Bar
                key={s.clave}
                dataKey={s.clave}
                name={s.etiqueta}
                stackId={agrupadas ? undefined : "total"}
                fill={s.color}
                maxBarSize={horizontal ? 16 : 28}
                radius={agrupadas || i === series.length - 1 ? (horizontal ? [0, 3, 3, 0] : [3, 3, 0, 0]) : 0}
              />
            ))}
          </BarChart>
        </ResponsiveContainer>
      </div>
      <LeyendaGrafica series={series} className="mt-2 justify-center" />
      <ul className="sr-only">
        {filas.map((f) => (
          <li key={f.etiqueta}>
            {f.etiqueta}: {series.map((s) => `${s.etiqueta} ${formato(Number(f[s.clave] ?? 0))}`).join(", ")}
          </li>
        ))}
      </ul>
    </div>
  );
}
