"use client";

import { useId } from "react";
import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { EJE_TICK, REJILLA, TOOLTIP_CONTENIDO, TOOLTIP_ETIQUETA, type SerieGrafica } from "./graficos-kit";
import { LeyendaGrafica } from "./LeyendaGrafica";

/** Un punto: lo que se lee en el eje X («nov 26») y un valor por cada `clave` de serie. */
export type PuntoArea = { etiqueta: string } & Record<string, number | string>;

export interface GraficoAreaProps {
  puntos: readonly PuntoArea[];
  series: readonly SerieGrafica[];
  /** Formato del eje Y (corto) y del tooltip (completo). */
  formatoEje?: (valor: number) => string;
  formato?: (valor: number) => string;
  /** Alto en px. */
  alto?: number;
}

/**
 * Gráfica de área del Inicio, copiada de `frontend/components/inicio/GraficoArea.tsx` (rediseño de Comparendos, S2) y
 * abierta a varias series (p. ej. ingresado contra pagado). El SVG es decorativo; la leyenda y una lista oculta son la
 * lectura accesible.
 */
export function GraficoArea({ puntos, series, formatoEje = String, formato = String, alto = 240 }: GraficoAreaProps) {
  const base = `area-${useId().replace(/:/g, "")}`;
  return (
    <div>
      <div className="-mx-2" style={{ height: alto }} aria-hidden="true">
        <ResponsiveContainer width="100%" height="100%">
          <AreaChart data={[...puntos]} margin={{ top: 8, right: 12, left: 0, bottom: 0 }}>
            <defs>
              {series.map((s, i) => (
                <linearGradient key={s.clave} id={`${base}-${i}`} x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor={s.color} stopOpacity={0.24} />
                  <stop offset="100%" stopColor={s.color} stopOpacity={0.02} />
                </linearGradient>
              ))}
            </defs>
            <CartesianGrid {...REJILLA} vertical={false} />
            <XAxis dataKey="etiqueta" tick={EJE_TICK} axisLine={false} tickLine={false} minTickGap={8} />
            <YAxis tick={EJE_TICK} axisLine={false} tickLine={false} tickFormatter={formatoEje} allowDecimals={false} width={64} />
            <Tooltip contentStyle={TOOLTIP_CONTENIDO} labelStyle={TOOLTIP_ETIQUETA} formatter={(v: number, nombre: string) => [formato(v), nombre]} />
            {series.map((s, i) => (
              <Area key={s.clave} type="monotone" dataKey={s.clave} name={s.etiqueta} stroke={s.color} strokeWidth={2} fill={`url(#${base}-${i})`} />
            ))}
          </AreaChart>
        </ResponsiveContainer>
      </div>
      {series.length > 1 && <LeyendaGrafica series={series} className="mt-2 justify-center" />}
      <ul className="sr-only">
        {puntos.map((p) => (
          <li key={p.etiqueta}>
            {p.etiqueta}: {series.map((s) => `${s.etiqueta} ${formato(Number(p[s.clave] ?? 0))}`).join(", ")}
          </li>
        ))}
      </ul>
    </div>
  );
}
