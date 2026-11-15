/**
 * Datos de una gráfica en una tabla solo para lectores de pantalla (spec-ui §4.6, WCAG 1.3.1 y 1.4.1): la gráfica es
 * `aria-hidden` y esta tabla dice lo mismo con encabezados de columna y de fila. Visualmente oculta (`sr-only`).
 */
export interface FilaAlternativa {
  encabezado: string;
  celdas: string[];
}

export function TablaAlternativa({ titulo, encabezados, filas }: { titulo: string; encabezados: string[]; filas: FilaAlternativa[] }) {
  return (
    <table className="sr-only">
      <caption>Datos de {titulo}</caption>
      <thead>
        <tr>
          {encabezados.map((e) => (
            <th key={e} scope="col">
              {e}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {filas.map((f, i) => (
          <tr key={`${f.encabezado}-${i}`}>
            <th scope="row">{f.encabezado}</th>
            {f.celdas.map((c, j) => (
              <td key={j}>{c}</td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
