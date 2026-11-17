/**
 * Datos de una gráfica en una tabla solo para lectores de pantalla (spec-ui §4.6, WCAG 1.3.1 y 1.4.1): la gráfica es
 * `aria-hidden` y esta tabla dice lo mismo con encabezados de columna y de fila. Visualmente oculta (`sr-only`) salvo
 * con `visible`, que la pinta con la cabecera y las líneas de la tabla del kit (auditoría UI UA-3, «Ver datos»).
 */
export interface FilaAlternativa {
  encabezado: string;
  celdas: string[];
}

const TABLA_VISIBLE_CLS = "w-full border-collapse text-xs text-[color:var(--di-text-primary)]";
const CABECERA_VISIBLE_CLS =
  "bg-[var(--table-header-bg)] px-2 py-1.5 font-semibold text-[color:var(--table-header-fg)] first:text-left [&:not(:first-child)]:text-right";
const CELDA_VISIBLE_CLS = "border-b border-[color:var(--table-row-border)] px-2 py-1.5 text-right tabular-nums";
const ENCABEZADO_FILA_VISIBLE_CLS = "border-b border-[color:var(--table-row-border)] px-2 py-1.5 text-left font-semibold";

export function TablaAlternativa({
  titulo,
  encabezados,
  filas,
  visible = false,
}: {
  titulo: string;
  encabezados: string[];
  filas: FilaAlternativa[];
  visible?: boolean;
}) {
  return (
    <table className={visible ? TABLA_VISIBLE_CLS : "sr-only"}>
      <caption className={visible ? "sr-only" : undefined}>Datos de {titulo}</caption>
      <thead>
        <tr>
          {encabezados.map((e) => (
            <th key={e} scope="col" className={visible ? CABECERA_VISIBLE_CLS : undefined}>
              {e}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {filas.map((f, i) => (
          <tr key={`${f.encabezado}-${i}`}>
            <th scope="row" className={visible ? ENCABEZADO_FILA_VISIBLE_CLS : undefined}>
              {f.encabezado}
            </th>
            {f.celdas.map((c, j) => (
              <td key={j} className={visible ? CELDA_VISIBLE_CLS : undefined}>
                {c}
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
