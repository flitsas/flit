"use client";

import { useEffect, useRef, useState } from "react";
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
  /**
   * Ancho MÁXIMO del eje de rótulos en horizontal (los nombres de organismo son largos). El real es el 22 % del ancho de
   * la gráfica entre 110 px y este tope, y el corte con «…» se ajusta a ese ancho; el tooltip da el nombre completo.
   */
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
  anchoRotulo = 280,
}: BarrasApiladasProps) {
  const horizontal = orientacion === "horizontal";
  const altura = alto ?? (horizontal ? Math.max(120, filas.length * altoFila + 32) : 260);
  const caja = useRef<HTMLDivElement>(null);
  const [ancho, setAncho] = useState(0);
  useEffect(() => {
    const el = caja.current;
    if (!el || typeof ResizeObserver === "undefined") return;
    const ro = new ResizeObserver(([e]) => setAncho(e.contentRect.width));
    ro.observe(el);
    return () => ro.disconnect();
  }, []);
  const rotulo = ancho > 0 ? Math.round(Math.min(Math.max(110, ancho * 0.22), anchoRotulo)) : Math.min(132, anchoRotulo);
  // ~6,3 px por carácter a 11 px: el nombre se corta a lo que cabe en dos líneas del eje.
  const maxCaracteres = Math.max(14, Math.floor((rotulo / 6.3) * 2));
  const recortar = (t: string) => (t.length > maxCaracteres ? `${t.slice(0, maxCaracteres - 1)}…` : t);
  return (
    <div>
      <div ref={caja} className="-mx-2" style={{ height: altura }} aria-hidden="true">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={[...filas]} layout={horizontal ? "vertical" : "horizontal"} margin={{ top: 4, right: 12, left: 0, bottom: 0 }}>
            <CartesianGrid {...REJILLA} horizontal={!horizontal} vertical={false} />
            {/* Sin fragmentos: recharts 2 busca los ejes entre sus hijos directos y no entra a un <>…</>. */}
            {horizontal ? (
              <XAxis type="number" tick={EJE_TICK} axisLine={false} tickLine={false} allowDecimals={false} tickFormatter={formatoEje} />
            ) : (
              <XAxis dataKey="etiqueta" tick={EJE_TICK} axisLine={false} tickLine={false} minTickGap={4} />
            )}
            {horizontal ? (
              <YAxis type="category" dataKey="etiqueta" tick={EJE_TICK} axisLine={false} tickLine={false} width={rotulo} tickFormatter={recortar} />
            ) : (
              <YAxis tick={EJE_TICK} axisLine={false} tickLine={false} allowDecimals={false} tickFormatter={formatoEje} width={84} />
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
