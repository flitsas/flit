"use client";

import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { EJE_TICK, REJILLA, TOOLTIP_CONTENIDO, TOOLTIP_CURSOR, TOOLTIP_ETIQUETA, type SerieGrafica } from "./graficos-kit";
import { LeyendaGrafica } from "./LeyendaGrafica";

/** Una fila de la gráfica: su rótulo («Matrícula inicial») y un valor por cada `clave` de serie. */
export type FilaApilada = { etiqueta: string } & Record<string, number | string>;

export interface BarrasApiladasProps {
  filas: readonly FilaApilada[];
  series: readonly SerieGrafica[];
  /** Alto por fila en px (la gráfica crece con las filas). */
  altoFila?: number;
}

/**
 * Barras horizontales apiladas del Inicio (SuperAdmin: «Distribución de operaciones», Completados / En proceso /
 * Rechazados por familia). La leyenda va debajo y es la lectura accesible de las series; cada fila se resume además
 * en una lista oculta para el lector de pantalla.
 */
export function BarrasApiladas({ filas, series, altoFila = 44 }: BarrasApiladasProps) {
  const alto = Math.max(120, filas.length * altoFila + 32);
  return (
    <div>
      <div className="-mx-2" style={{ height: alto }} aria-hidden="true">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={[...filas]} layout="vertical" margin={{ top: 4, right: 12, left: 8, bottom: 0 }}>
            <CartesianGrid {...REJILLA} horizontal={false} />
            <XAxis type="number" tick={EJE_TICK} axisLine={false} tickLine={false} allowDecimals={false} />
            <YAxis type="category" dataKey="etiqueta" tick={EJE_TICK} axisLine={false} tickLine={false} width={116} />
            <Tooltip cursor={TOOLTIP_CURSOR} contentStyle={TOOLTIP_CONTENIDO} labelStyle={TOOLTIP_ETIQUETA} />
            {series.map((s, i) => (
              <Bar
                key={s.clave}
                dataKey={s.clave}
                name={s.etiqueta}
                stackId="total"
                fill={s.color}
                barSize={16}
                radius={i === series.length - 1 ? [0, 3, 3, 0] : 0}
              />
            ))}
          </BarChart>
        </ResponsiveContainer>
      </div>
      <LeyendaGrafica series={series} className="mt-2 justify-center" />
      <ul className="sr-only">
        {filas.map((f) => (
          <li key={f.etiqueta}>
            {f.etiqueta}: {series.map((s) => `${s.etiqueta} ${Number(f[s.clave] ?? 0).toLocaleString("es-CO")}`).join(", ")}
          </li>
        ))}
      </ul>
    </div>
  );
}
