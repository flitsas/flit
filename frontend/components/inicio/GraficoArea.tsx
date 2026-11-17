"use client";

import { useId } from "react";
import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { EJE_TICK, REJILLA, TOOLTIP_CONTENIDO, TOOLTIP_ETIQUETA } from "./graficos-kit";

export interface PuntoArea {
  /** Lo que se lee en el eje X («17 nov», «Sem 46»). */
  etiqueta: string;
  valor: number;
}

export interface GraficoAreaProps {
  puntos: readonly PuntoArea[];
  /** Nombre de la serie en el tooltip y en el resumen accesible («Tiempo promedio de decisión»). */
  nombreSerie: string;
  /** Formato del valor en el eje Y y el tooltip (p. ej. `(v) => `${v} d``). */
  formato?: (valor: number) => string;
  color?: string;
  /** Alto en px. */
  alto?: number;
}

/**
 * Gráfica de área del Inicio (OT: «Tiempos de gestión»). Sin línea de meta: no hay SLA en el dominio (decisión del
 * 2026-11-17), así que se pinta solo la serie. El SVG es decorativo; el lector de pantalla recibe un resumen con los
 * puntos en una lista oculta.
 */
export function GraficoArea({
  puntos,
  nombreSerie,
  formato = (v) => String(v),
  color = "var(--color-flit-brand)",
  alto = 220,
}: GraficoAreaProps) {
  const idDegradado = `area-${useId().replace(/:/g, "")}`;
  return (
    <div>
      <div className="-mx-2" style={{ height: alto }} aria-hidden="true">
        <ResponsiveContainer width="100%" height="100%">
          <AreaChart data={[...puntos]} margin={{ top: 8, right: 12, left: -12, bottom: 0 }}>
            <defs>
              <linearGradient id={idDegradado} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={color} stopOpacity={0.28} />
                <stop offset="100%" stopColor={color} stopOpacity={0.02} />
              </linearGradient>
            </defs>
            <CartesianGrid {...REJILLA} vertical={false} />
            <XAxis dataKey="etiqueta" tick={EJE_TICK} axisLine={false} tickLine={false} />
            <YAxis tick={EJE_TICK} axisLine={false} tickLine={false} tickFormatter={formato} allowDecimals={false} />
            <Tooltip
              contentStyle={TOOLTIP_CONTENIDO}
              labelStyle={TOOLTIP_ETIQUETA}
              formatter={(v: number) => [formato(v), nombreSerie]}
            />
            <Area
              type="monotone"
              dataKey="valor"
              name={nombreSerie}
              stroke={color}
              strokeWidth={2}
              fill={`url(#${idDegradado})`}
            />
          </AreaChart>
        </ResponsiveContainer>
      </div>
      <ul className="sr-only">
        {puntos.map((p) => (
          <li key={p.etiqueta}>
            {p.etiqueta}: {formato(p.valor)}
          </li>
        ))}
      </ul>
    </div>
  );
}
