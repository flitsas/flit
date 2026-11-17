import { formatoEntero, porcentajeDe } from "@/lib/estadisticas/formato";
import { Figura, Leyenda } from "./Figura";
import { TablaAlternativa } from "./TablaAlternativa";

export interface Segmento {
  nombre: string;
  valor: number;
  /** Token de color (`var(--di-success-dot)`…); nunca un hex. */
  color: string;
}

const texto = (s: Segmento, total: number) => `${s.nombre}: ${formatoEntero(s.valor)} (${porcentajeDe(s.valor, total)})`;

/**
 * Barra horizontal apilada con leyenda de cifra y porcentaje (spec-ui §4.3 `serie.veredictos` y `serie.tipoAnalisis`).
 * Los segmentos se separan 2 px y llevan su valor en `title`; la barra es decorativa y la tabla alternativa repite los
 * datos. Con total 0 se pinta la pista vacía.
 */
export function BarraApilada({ titulo, subtitulo, segmentos, nivel = 3 }: { titulo: string; subtitulo?: string; segmentos: Segmento[]; nivel?: 2 | 3 }) {
  const total = segmentos.reduce((suma, s) => suma + s.valor, 0);
  return (
    <Figura titulo={titulo} subtitulo={subtitulo} nivel={nivel}>
      <div aria-hidden="true" className="flex h-4 w-full gap-[2px] overflow-hidden rounded-full bg-[var(--di-surface-sunken)]">
        {total > 0
          ? segmentos
              .filter((s) => s.valor > 0)
              .map((s) => <div key={s.nombre} title={texto(s, total)} className="h-full" style={{ width: `${(s.valor / total) * 100}%`, background: s.color }} />)
          : null}
      </div>
      <Leyenda items={segmentos.map((s) => ({ nombre: s.nombre, color: s.color, texto: texto(s, total) }))} />
      <TablaAlternativa
        titulo={titulo}
        encabezados={["Categoría", "Cantidad", "Porcentaje"]}
        filas={segmentos.map((s) => ({ encabezado: s.nombre, celdas: [formatoEntero(s.valor), porcentajeDe(s.valor, total)] }))}
      />
    </Figura>
  );
}
