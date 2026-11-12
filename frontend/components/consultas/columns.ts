// Maquinaria común de los informes con columnas a elección.
//
// El informe del periodo y el de revisores comparten todo salvo qué columnas tienen: misma tabla,
// mismo selector, mismo CSV y mismo Excel. Esto vive aquí para que sea LITERALMENTE el mismo código
// y no dos implementaciones que se parecen — que es como se llega a que el CSV de un informe
// neutralice las fórmulas y el del otro no.

import type { MaxActoresPorRol } from "@/lib/api/company-queries";
import { buildXlsx, type XlsxCell } from "@/lib/xlsx";

/**
 * Columna que en pantalla es UNA y en el archivo se abre en varios bloques, uno por actor del trámite.
 *
 * Las columnas de un mismo `grupo` (p. ej. Comprador, Validación comprador, Intentos comprador…) se
 * escriben juntas, persona por persona, EN EL SITIO de la primera columna visible del grupo. El
 * ancho (cuántas personas) lo fija `maxActoresPorRol` sobre todo el filtro, no sobre las filas del
 * archivo: así todas las partes de un export por lotes salen con las mismas columnas.
 */
export interface ExpansionPorBloque<TRow> {
  grupo: string;
  /**
   * Cuántos bloques ocupa esta columna según el máximo del servidor. `0` la omite: sin máximo no se
   * inventa el ancho de las columnas nuevas. La columna de nombre devuelve al menos 1 incluso sin
   * máximo, porque «Comprador» siempre existió.
   */
  bloques: (max: MaxActoresPorRol | undefined) => number;
  /** Encabezado del bloque n (1-based): «Comprador», «Comprador 2»… */
  header: (n: number) => string;
  width?: number;
  /** Valor del bloque n de esta fila; `null` si ese actor no existe. */
  cell: (row: TRow, n: number) => XlsxCell;
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
  /** Si está, en el archivo esta columna se abre por actor, en su sitio. */
  expandirPorBloque?: ExpansionPorBloque<TRow>;
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
  const salida = planSalida(visibleColumns(columns, visibleIds), maxActoresPorRol);
  const header = salida.map((c) => csvCell(c.headerCsv)).join(";");
  const body = rows.map((row) => salida.map((c) => csvCell(c.csv(row))).join(";"));
  return `\ufeff${[header, ...body].join("\r\n")}`;
}

interface ColumnaSalida<TRow> {
  headerCsv: string;
  headerXlsx: string;
  width?: number;
  csv: (row: TRow) => string;
  xlsx: (row: TRow) => XlsxCell;
}

/**
 * Las columnas del archivo, en orden. Las normales van tal cual; las que se abren por actor se
 * escriben en el sitio de la primera visible de su grupo, persona por persona.
 *
 * Sin `maxActoresPorRol` las columnas por actor que no existían antes se omiten: inventar el ancho
 * desde las filas a mano haría que cada parte de un export por lotes saliera distinta.
 */
function planSalida<TRow>(
  cols: DataColumn<TRow>[],
  max: MaxActoresPorRol | undefined,
): ColumnaSalida<TRow>[] {
  const salida: ColumnaSalida<TRow>[] = [];
  const yaEscritos = new Set<string>();

  for (const col of cols) {
    const exp = col.expandirPorBloque;
    if (!exp) {
      salida.push({
        headerCsv: col.label,
        headerXlsx: col.xlsxHeader ?? col.label,
        width: col.width,
        csv: (row) => col.value(row),
        xlsx: (row) => (col.raw ? col.raw(row) : col.value(row)),
      });
      continue;
    }
    if (yaEscritos.has(exp.grupo)) continue;
    yaEscritos.add(exp.grupo);

    const delGrupo = cols.filter((c) => c.expandirPorBloque?.grupo === exp.grupo);
    const ancho = Math.max(...delGrupo.map((c) => c.expandirPorBloque!.bloques(max)));
    for (let n = 1; n <= ancho; n += 1) {
      for (const c of delGrupo) {
        const e = c.expandirPorBloque!;
        if (e.bloques(max) < n) continue;
        salida.push({
          headerCsv: e.header(n),
          headerXlsx: e.header(n),
          width: e.width ?? c.width,
          csv: (row) => celdaTexto(e.cell(row, n)),
          xlsx: (row) => e.cell(row, n),
        });
      }
    }
  }
  return salida;
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
  const salida = planSalida(visibleColumns(columns, visibleIds), maxActoresPorRol);
  return buildXlsx({
    name: sheetName,
    columns: salida.map((c) => ({ header: c.headerXlsx, width: c.width })),
    rows: rows.map((row) => salida.map((c) => c.xlsx(row))),
    notes,
  });
}

/** Nombre con el rango dentro: un `informe.xlsx` suelto en Descargas no dice de cuándo es. */
export function rangedFileName(prefix: string, from: string, to: string, ext: "csv" | "xlsx"): string {
  return `${prefix}-${from}-a-${to}.${ext}`;
}
