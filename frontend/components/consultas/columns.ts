// Maquinaria común de los informes con columnas a elección.
//
// El informe del periodo y el de revisores comparten todo salvo qué columnas tienen: misma tabla,
// mismo selector, mismo CSV y mismo Excel. Esto vive aquí para que sea LITERALMENTE el mismo código
// y no dos implementaciones que se parecen — que es como se llega a que el CSV de un informe
// neutralice las fórmulas y el del otro no.

import type { ActorValidacionDto, MaxActoresPorRol, RolActor } from "@/lib/api/company-queries";
import { buildXlsx, type XlsxCell } from "@/lib/xlsx";

/**
 * Columna que en pantalla es UNA y en el archivo se abre en varias, una por actor del trámite.
 *
 * Los archivos agrupan por persona (nombre, documento, validación… de un actor, luego el siguiente)
 * y van AL FINAL de la hoja, después de las columnas normales. El ancho lo fija `maxActoresPorRol`
 * sobre todo el filtro, no sobre las filas del archivo: así todas las partes de un export por lotes
 * salen con las mismas columnas.
 */
export interface ExpansionPorActor<TRow> {
  roles: RolActor[];
  actoresDe: (row: TRow) => ActorValidacionDto[] | undefined;
  campos: {
    header: (rol: RolActor, n: number) => string;
    width?: number;
    cell: (actor: ActorValidacionDto) => XlsxCell;
  }[];
}

/**
 * Una columna de un informe exportable.
 *
 * `value` produce el texto que se ve y que va al CSV; `raw` produce el valor tipado que va al Excel.
 * Tener los dos en la MISMA definición es lo que garantiza que lo exportado sea lo que se está
 * viendo: con dos listas paralelas, el día que alguien añade una columna a la tabla y olvida el
 * export, nadie se entera hasta que un informe llega mal a una reunión.
 */
export interface DataColumn<TRow, TSort extends string = string> {
  id: string;
  label: string;
  /** Grupo del selector: agrupa por la pregunta que responde la columna, no por tipo de dato. */
  group: string;
  value: (row: TRow) => string;
  /** Valor tipado para Excel. Sin esto, el .xlsx sería un CSV con otra extensión. */
  raw?: (row: TRow) => XlsxCell;
  /** Encabezado alternativo en Excel, para columnas cuya unidad vive dentro de la celda. */
  xlsxHeader?: string;
  /** Ancho en Excel, en caracteres. */
  width?: number;
  /** Clave de orden del backend, si la columna es ordenable. */
  sort?: TSort;
  numeric?: boolean;
  defaultVisible?: boolean;
  /** Si está, en el archivo esta columna se abre por actor. Sin `maxActoresPorRol`, no se exporta. */
  expandirPorActor?: ExpansionPorActor<TRow>;
}

export interface ColumnPreset {
  id: string;
  label: string;
  hint: string;
  columns: string[];
}

export function defaultVisible<TRow>(columns: DataColumn<TRow>[]): string[] {
  return columns.filter((c) => c.defaultVisible).map((c) => c.id);
}

/** Grupos en el orden en que aparecen en la definición, sin repetir. */
export function groupsOf<TRow>(columns: DataColumn<TRow>[]): string[] {
  return [...new Set(columns.map((c) => c.group))];
}

export function visibleColumns<TRow>(
  columns: DataColumn<TRow>[],
  visibleIds: string[],
): DataColumn<TRow>[] {
  return columns.filter((c) => visibleIds.includes(c.id));
}

/**
 * Cuál preset describe la selección actual, o `null` si es una combinación propia.
 *
 * Se compara como CONJUNTO y no como lista: el selector devuelve los ids en el orden canónico de la
 * definición, que no tiene por qué coincidir con el orden en que se escribió el preset. Comparando
 * listas, elegir una vista dejaría de marcarse a sí misma un segundo después.
 */
export function activePreset(presets: ColumnPreset[], visibleIds: string[]): string | null {
  const visible = new Set(visibleIds);
  const preset = presets.find(
    (p) => p.columns.length === visible.size && p.columns.every((id) => visible.has(id)),
  );
  return preset?.id ?? null;
}

// ── Exportación ────────────────────────────────────────────────────────────────

/**
 * Neutraliza el prefijo de fórmula. Un valor que empieza por `=`, `+`, `-` o `@` lo ejecuta Excel al
 * abrir el archivo: es una vía de inyección real, y aquí los valores vienen de campos que la empresa
 * cliente escribe.
 */
function neutralize(value: string): string {
  return /^[=+\-@\t\r]/.test(value) ? `'${value}` : value;
}

function csvCell(value: string): string {
  return `"${neutralize(value).replace(/"/g, '""')}"`;
}

/**
 * CSV de lo que se está viendo: mismas columnas, mismo orden, mismo formateo.
 *
 * Separador `;` y BOM porque el destino real es Excel en español, que con `,` mete la fila entera en
 * una sola celda y sin BOM rompe las tildes.
 */
export function buildCsv<TRow>(
  columns: DataColumn<TRow>[],
  rows: TRow[],
  visibleIds: string[],
  maxActoresPorRol?: MaxActoresPorRol,
): string {
  const { normales, expandidas } = planSalida(visibleColumns(columns, visibleIds), maxActoresPorRol);
  const header = [...normales.map((c) => c.label), ...expandidas.map((e) => e.header)]
    .map(csvCell)
    .join(";");
  const body = rows.map((row) =>
    [...normales.map((c) => c.value(row)), ...expandidas.map((e) => celdaTexto(e.cell(row)))]
      .map(csvCell)
      .join(";"),
  );
  return `\ufeff${[header, ...body].join("\r\n")}`;
}

interface CeldaExpandida<TRow> {
  header: string;
  width?: number;
  cell: (row: TRow) => XlsxCell;
}

/**
 * Separa las columnas normales de las que se abren por actor y calcula, en orden de bloque
 * (rol, luego actor n-ésimo, luego campos), las columnas extra del archivo.
 *
 * Sin `maxActoresPorRol` las columnas por actor se omiten: inventar el ancho desde las filas que
 * hay a mano haría que cada parte de un export por lotes saliera distinta.
 */
function planSalida<TRow>(
  cols: DataColumn<TRow>[],
  max: MaxActoresPorRol | undefined,
): { normales: DataColumn<TRow>[]; expandidas: CeldaExpandida<TRow>[] } {
  const normales = cols.filter((c) => !c.expandirPorActor);
  const conActores = cols.filter((c) => c.expandirPorActor);
  const expandidas: CeldaExpandida<TRow>[] = [];
  if (!max || conActores.length === 0) return { normales, expandidas };

  const roles = conActores[0].expandirPorActor!.roles;
  for (const rol of roles) {
    for (let n = 1; n <= (max[rol] ?? 0); n += 1) {
      for (const col of conActores) {
        const spec = col.expandirPorActor!;
        for (const campo of spec.campos) {
          expandidas.push({
            header: campo.header(rol, n),
            width: campo.width,
            cell: (row) => {
              const actor = (spec.actoresDe(row) ?? [])
                .filter((a) => a.rol === rol)
                .sort((x, y) => x.ordinal - y.ordinal)[n - 1];
              return actor ? campo.cell(actor) : null;
            },
          });
        }
      }
    }
  }
  return { normales, expandidas };
}

function celdaTexto(cell: XlsxCell): string {
  return cell === null || typeof cell === "object" ? "" : String(cell);
}

/**
 * El MISMO informe como libro de Excel, que es donde acaba de verdad.
 *
 * La diferencia con el CSV no es el envoltorio: aquí los números van como números y las fechas como
 * fechas, así que se puede sumar una columna u ordenar cronológicamente sin tocar nada. Las celdas
 * sin dato van vacías y nunca con un guion: un «—» en columna numérica la vuelve texto para toda la
 * hoja y rompe cualquier suma.
 */
export function buildWorkbook<TRow>(
  sheetName: string,
  columns: DataColumn<TRow>[],
  rows: TRow[],
  visibleIds: string[],
  notes?: string[],
  maxActoresPorRol?: MaxActoresPorRol,
): Uint8Array<ArrayBuffer> {
  const { normales, expandidas } = planSalida(visibleColumns(columns, visibleIds), maxActoresPorRol);
  return buildXlsx({
    name: sheetName,
    columns: [
      ...normales.map((c) => ({ header: c.xlsxHeader ?? c.label, width: c.width })),
      ...expandidas.map((e) => ({ header: e.header, width: e.width })),
    ],
    rows: rows.map((row) => [
      ...normales.map((c) => (c.raw ? c.raw(row) : c.value(row))),
      ...expandidas.map((e) => e.cell(row)),
    ]),
    notes,
  });
}

/** Nombre con el rango dentro: un `informe.xlsx` suelto en Descargas no dice de cuándo es. */
export function rangedFileName(prefix: string, from: string, to: string, ext: "csv" | "xlsx"): string {
  return `${prefix}-${from}-a-${to}.${ext}`;
}
