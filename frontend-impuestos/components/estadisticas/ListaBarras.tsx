import { FOCO_CLS } from "@/components/comun/estilos";
import { formatoEntero } from "@/lib/estadisticas/formato";
import { Figura } from "./Figura";
import { TablaAlternativa } from "./TablaAlternativa";

// Copia local de la forma de frontend-diagnostico/components/estadisticas/graficas/ListaBarras.tsx (D-UI-GRAFICAS).

export interface ItemBarra {
  clave: string;
  nombre: string;
  valor: number;
  /** Línea secundaria en texto. */
  detalle?: string;
  /** Token de color de la barra; por defecto `var(--chart-1)`. */
  color?: string;
  /** Celdas extra de la tabla alternativa, en el orden de `encabezadosExtra`. */
  extra?: string[];
}

const PISTA_CLS = "h-2 w-full overflow-hidden rounded-full bg-[var(--di-surface-sunken)]";

/**
 * Lista de barras horizontales con el nombre y la cifra en texto. Sin acción, la lista visual es decorativa y los datos
 * van en la tabla alternativa. Con `onElegir` cada fila es un botón (el ranking elige esa compañía) y la lista es la
 * propia alternativa: no se duplica.
 */
export function ListaBarras({
  titulo,
  subtitulo,
  items,
  nivel = 3,
  onElegir,
  etiquetaAccion,
  encabezadoCategoria = "Categoría",
  encabezadoValor = "Cantidad",
  encabezadosExtra = [],
}: {
  titulo: string;
  subtitulo?: string;
  items: ItemBarra[];
  nivel?: 2 | 3;
  onElegir?: (clave: string) => void;
  etiquetaAccion?: (item: ItemBarra) => string;
  /** Encabezados de la tabla alternativa (por defecto «Categoría» y «Cantidad»). */
  encabezadoCategoria?: string;
  encabezadoValor?: string;
  encabezadosExtra?: string[];
}) {
  const maximo = Math.max(0, ...items.map((i) => i.valor));
  const ancho = (v: number) => (maximo > 0 ? `${(v / maximo) * 100}%` : "0%");

  const fila = (i: ItemBarra) => (
    <>
      <span className="flex items-baseline justify-between gap-3 text-sm">
        <span className="truncate text-[color:var(--di-text-primary)]" title={i.nombre}>
          {i.nombre}
        </span>
        <span className="shrink-0 font-semibold tabular-nums text-[color:var(--di-text-primary)]">{formatoEntero(i.valor)}</span>
      </span>
      <span aria-hidden="true" className={PISTA_CLS}>
        <span className="block h-full rounded-full" style={{ width: ancho(i.valor), background: i.color ?? "var(--chart-1)" }} />
      </span>
      {i.detalle ? <span className="text-xs text-[color:var(--di-text-secondary)]">{i.detalle}</span> : null}
    </>
  );

  return (
    <Figura titulo={titulo} subtitulo={subtitulo} nivel={nivel}>
      {onElegir ? (
        <ul className="m-0 flex list-none flex-col gap-1 p-0">
          {items.map((i) => (
            <li key={i.clave}>
              <button
                type="button"
                onClick={() => onElegir(i.clave)}
                aria-label={etiquetaAccion?.(i)}
                className={`flex min-h-6 w-full flex-col gap-1 rounded-[9px] px-2 py-1.5 text-left hover:bg-[var(--kit-hover)] ${FOCO_CLS}`}
              >
                {fila(i)}
              </button>
            </li>
          ))}
        </ul>
      ) : (
        <>
          <ul aria-hidden="true" className="m-0 flex list-none flex-col gap-2 p-0">
            {items.map((i) => (
              <li key={i.clave} className="flex flex-col gap-1">
                {fila(i)}
              </li>
            ))}
          </ul>
          <TablaAlternativa
            titulo={titulo}
            encabezados={[encabezadoCategoria, encabezadoValor, ...encabezadosExtra]}
            filas={items.map((i) => ({
              encabezado: i.nombre,
              celdas: [formatoEntero(i.valor), ...encabezadosExtra.map((_, k) => i.extra?.[k] ?? "—")],
            }))}
          />
        </>
      )}
    </Figura>
  );
}
