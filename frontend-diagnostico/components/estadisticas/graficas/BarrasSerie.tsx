import { formatoEntero } from "@/lib/estadisticas/formato";
import { Figura, Leyenda } from "./Figura";
import { TablaAlternativa } from "./TablaAlternativa";

export interface SerieBarras {
  nombre: string;
  /** Token de color (`var(--chart-1)`…); nunca un hex. */
  color: string;
  valores: number[];
}

/** Rótulos del eje como máximo: con más cubetas se rotula una de cada N para que no se encimen. */
const ROTULOS_MAXIMOS = 8;

/** Desplegable «Ver datos» (auditoría UI UA-3): enlace de texto del kit con foco visible. */
const RESUMEN_CLS =
  "w-fit cursor-pointer rounded-sm font-semibold text-[color:var(--kit-azul-texto)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[color:var(--di-action)]";

/**
 * Columnas agrupadas por cubeta (spec-ui §4.3 `serie.temporal`, opción A de §4.6: CSS sin dependencias). Las barras son
 * decorativas (`aria-hidden`) con su valor en `title`; los datos van en texto en la leyenda y en la tabla de «Ver datos»,
 * visible al desplegarla (auditoría UI UA-3).
 */
export function BarrasSerie({
  titulo,
  subtitulo,
  etiquetas,
  series,
  nivel = 3,
}: {
  titulo: string;
  subtitulo?: string;
  etiquetas: string[];
  series: SerieBarras[];
  nivel?: 2 | 3;
}) {
  const maximo = Math.max(0, ...series.flatMap((s) => s.valores));
  const paso = Math.max(1, Math.ceil(etiquetas.length / ROTULOS_MAXIMOS));
  const alto = (v: number) => (maximo > 0 ? `${(v / maximo) * 100}%` : "0%");

  return (
    <Figura titulo={titulo} subtitulo={subtitulo} nivel={nivel}>
      <Leyenda items={series} />
      <div data-parte="barras" aria-hidden="true" className="flex flex-col gap-1">
        <div className="flex h-48 items-end gap-1 border-b border-[color:var(--di-divider)]">
          {etiquetas.map((etiqueta, i) => (
            <div key={`${etiqueta}-${i}`} className="flex h-full min-w-0 flex-1 items-end justify-center gap-[2px]">
              {series.map((s) => {
                const v = s.valores[i] ?? 0;
                return (
                  <div
                    key={s.nombre}
                    title={`${s.nombre} · ${etiqueta}: ${formatoEntero(v)}`}
                    className={`w-full max-w-4 rounded-t-sm ${v > 0 ? "min-h-[2px]" : ""}`}
                    style={{ height: alto(v), background: s.color }}
                  />
                );
              })}
            </div>
          ))}
        </div>
        <div className="flex gap-1 text-xs text-[color:var(--di-text-secondary)]">
          {etiquetas.map((etiqueta, i) => (
            <span key={`${etiqueta}-${i}`} className="min-w-0 flex-1 truncate text-center">
              {i % paso === 0 ? etiqueta : ""}
            </span>
          ))}
        </div>
      </div>
      <details className="group text-xs">
        <summary className={RESUMEN_CLS}>Ver datos</summary>
        <div className="mt-2 max-h-64 overflow-auto">
          <TablaAlternativa
            visible
            titulo={titulo}
            encabezados={["Periodo", ...series.map((s) => s.nombre)]}
            filas={etiquetas.map((etiqueta, i) => ({ encabezado: etiqueta, celdas: series.map((s) => formatoEntero(s.valores[i] ?? 0)) }))}
          />
        </div>
      </details>
    </Figura>
  );
}
