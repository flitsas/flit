import { Fragment } from "react";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import { CABECERA_TABLA_CLS, CABECERA_TABLA_ESTILO, CELDA_TABLA_CLS, CONTENEDOR_TABLA_CLS, FILA_TABLA_CLS, FOCO_CLS } from "@/components/comun/estilos";
import { Marca } from "@/components/liquidaciones/ChipEstado";
import { COLUMNAS_PERIODO } from "@/lib/liquidaciones/estados";
import { SIN_DATO, esSinDato, formatoFechaDia, formatoMonto } from "@/lib/liquidaciones/formato";
import type { Periodo } from "@/lib/liquidaciones/tipos";

/** Monto: alineado a la derecha con cifras tabulares; `null` dice «Sin dato» en tinta terciaria, nunca 0 (F2b-CF7). */
export function Monto({ valor, className = "" }: { valor: number | null | undefined; className?: string }) {
  if (esSinDato(valor)) return <span className={`text-[color:var(--di-text-tertiary)] ${className}`}>{SIN_DATO}</span>;
  return <span className={`tabular-nums ${className}`}>{formatoMonto(valor)}</span>;
}

const MONTOS = ["avaluo", "impuesto", "sancion", "intereses", "descuentos", "pagosPrevios", "totalAPagar"] as const;
const CELDA_CLS = `${CELDA_TABLA_CLS} ${TABLA_MOVIL_CELL_CLS}`;

/**
 * Vigencias adeudadas (spec-visual §3.6). Columnas con las etiquetas del contrato (Periodo.x-flit-etiquetas); montos a la
 * derecha y cada `null` «Sin dato». Una vigencia no liquidable lleva el literal del contrato. `otrosCargos` va como
 * sub-filas desplegables. Contenedor con scroll horizontal enfocable (región «Vigencias»); por debajo de 1024 px se apila.
 */
export function TablaVigencias({ periodos }: { periodos: Periodo[] }) {
  if (periodos.length === 0) return <p className="text-sm text-muted-foreground">Sin vigencias pendientes</p>;
  const columnas = [COLUMNAS_PERIODO.periodo, ...MONTOS.map((m) => COLUMNAS_PERIODO[m]), COLUMNAS_PERIODO.vencimiento, COLUMNAS_PERIODO.referenciaPago];
  return (
    <div tabIndex={0} role="region" aria-label="Vigencias" className={`${CONTENEDOR_TABLA_CLS} ${FOCO_CLS}`}>
      <table aria-label="Vigencias" className={`w-full min-w-[980px] border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
        <thead className={TABLA_MOVIL_THEAD_CLS}>
          <tr>
            {columnas.map((t, i) => (
              <th key={t} scope="col" className={`${CABECERA_TABLA_CLS} ${i > 0 && i <= MONTOS.length ? "text-right" : ""}`} style={CABECERA_TABLA_ESTILO}>
                {t}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className={TABLA_MOVIL_TBODY_CLS}>
          {periodos.map((p) => (
            <Fragment key={p.periodo}>
              <tr className={`${FILA_TABLA_CLS} bg-card ${TABLA_MOVIL_ROW_CLS}`}>
                <th scope="row" data-label={COLUMNAS_PERIODO.periodo} className={`${CELDA_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS} text-left font-semibold`}>
                  <span className="flex flex-col items-start gap-1">
                    {p.periodo}
                    {p.noLiquidable ? <Marca tono="neutral">{COLUMNAS_PERIODO.noLiquidable}</Marca> : null}
                  </span>
                </th>
                {MONTOS.map((m) => (
                  <td key={m} data-label={COLUMNAS_PERIODO[m]} className={`${CELDA_CLS} text-right ${m === "totalAPagar" ? "font-semibold" : ""}`}>
                    <Monto valor={p[m]} />
                  </td>
                ))}
                <td data-label={COLUMNAS_PERIODO.vencimiento} className={`${CELDA_CLS} whitespace-nowrap tabular-nums`}>
                  {p.vencimiento ? <time dateTime={p.vencimiento}>{formatoFechaDia(p.vencimiento)}</time> : <Monto valor={null} />}
                </td>
                <td data-label={COLUMNAS_PERIODO.referenciaPago} className={`${CELDA_CLS} font-mono`}>
                  {p.referenciaPago || <Monto valor={null} />}
                </td>
              </tr>
              {p.otrosCargos.length > 0 ? (
                <tr className={`${FILA_TABLA_CLS} bg-card`}>
                  <td colSpan={columnas.length} className={CELDA_TABLA_CLS}>
                    <details>
                      <summary className={`cursor-pointer font-semibold ${FOCO_CLS}`}>{`${COLUMNAS_PERIODO.otrosCargos} (${p.otrosCargos.length})`}</summary>
                      <ul className="mt-1 flex flex-col gap-1">
                        {p.otrosCargos.map((c, i) => (
                          <li key={`${c.descripcion}-${i}`} className="flex justify-between gap-4">
                            <span>{c.descripcion}</span>
                            <Monto valor={c.monto} />
                          </li>
                        ))}
                      </ul>
                    </details>
                  </td>
                </tr>
              ) : null}
            </Fragment>
          ))}
        </tbody>
      </table>
    </div>
  );
}
