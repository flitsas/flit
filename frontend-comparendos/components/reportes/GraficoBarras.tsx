"use client";

import { numeroCorto, topeEje } from "@/lib/reportes";

export interface SerieGrafico {
  nombre: string;
  color: string;
  valores: number[];
}

/**
 * Barras en SVG propio (el monorepo no trae librería de gráficas): agrupadas (una barra por serie, p. ej. año actual
 * contra anterior) o apiladas (p. ej. pagos por tipo). Escala con el ancho del contenedor; cada barra lleva su
 * <title> para leer el valor exacto al pasar el mouse.
 */
export function GraficoBarras({
  titulo,
  categorias,
  series,
  apilado = false,
  formato = (n: number) => n.toLocaleString("es-CO"),
}: {
  titulo: string;
  categorias: string[];
  series: SerieGrafico[];
  apilado?: boolean;
  formato?: (n: number) => string;
}) {
  const ancho = 640;
  const alto = 220;
  const margen = { arriba: 12, derecha: 8, abajo: 26, izquierda: 48 };
  const areaAncho = ancho - margen.izquierda - margen.derecha;
  const areaAlto = alto - margen.arriba - margen.abajo;

  const totales = categorias.map((_, i) =>
    apilado ? series.reduce((s, x) => s + (x.valores[i] ?? 0), 0) : Math.max(0, ...series.map((x) => x.valores[i] ?? 0)),
  );
  const tope = topeEje(Math.max(0, ...totales));
  const y = (v: number) => margen.arriba + areaAlto - (v / tope) * areaAlto;
  const banda = areaAncho / Math.max(1, categorias.length);
  const anchoGrupo = banda * 0.7;
  const anchoBarra = apilado ? anchoGrupo : anchoGrupo / Math.max(1, series.length);
  const marcas = [0, 0.25, 0.5, 0.75, 1].map((f) => f * tope);
  const vacio = totales.every((t) => t === 0);

  return (
    <figure className="flex flex-col gap-2">
      <figcaption className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-sm font-semibold text-[#162744] dark:text-white">{titulo}</span>
        {series.length > 1 && (
          <span className="flex flex-wrap items-center gap-3 text-[11px] text-[#5B6B82] dark:text-slate-300">
            {series.map((s) => (
              <span key={s.nombre} className="inline-flex items-center gap-1">
                <span className="inline-block h-2.5 w-2.5 rounded-sm" style={{ backgroundColor: s.color }} aria-hidden />
                {s.nombre}
              </span>
            ))}
          </span>
        )}
      </figcaption>
      <svg viewBox={`0 0 ${ancho} ${alto}`} className="h-auto w-full" role="img" aria-label={titulo}>
        {marcas.map((m) => (
          <g key={m}>
            <line x1={margen.izquierda} x2={ancho - margen.derecha} y1={y(m)} y2={y(m)} className="stroke-[#EEF1F6] dark:stroke-white/10" strokeWidth={1} />
            <text x={margen.izquierda - 6} y={y(m) + 3} textAnchor="end" className="fill-[#5B6B82] text-[10px] dark:fill-slate-400">
              {numeroCorto(m)}
            </text>
          </g>
        ))}
        {categorias.map((c, i) => {
          const x0 = margen.izquierda + i * banda + (banda - anchoGrupo) / 2;
          let acumulado = 0;
          return (
            <g key={`${c}-${i}`}>
              {series.map((s, j) => {
                const v = s.valores[i] ?? 0;
                const base = apilado ? acumulado : 0;
                if (apilado) acumulado += v;
                const x = apilado ? x0 : x0 + j * anchoBarra;
                const alturaBarra = Math.max(0, y(base) - y(base + v));
                return (
                  <rect key={s.nombre} x={x} y={y(base + v)} width={Math.max(1, anchoBarra - (apilado ? 0 : 2))} height={alturaBarra} rx={2} fill={s.color}>
                    <title>{`${c} · ${s.nombre}: ${formato(v)}`}</title>
                  </rect>
                );
              })}
              <text x={margen.izquierda + i * banda + banda / 2} y={alto - 8} textAnchor="middle" className="fill-[#5B6B82] text-[10px] dark:fill-slate-400">
                {c}
              </text>
            </g>
          );
        })}
        {vacio && (
          <text x={margen.izquierda + areaAncho / 2} y={margen.arriba + areaAlto / 2} textAnchor="middle" className="fill-[#5B6B82] text-[12px] dark:fill-slate-400">
            Sin datos en el periodo
          </text>
        )}
      </svg>
    </figure>
  );
}
