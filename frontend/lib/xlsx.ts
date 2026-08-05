// Escritor mínimo de XLSX.
//
// Por qué no una librería: las dos candidatas del ecosistema son SheetJS —cuyo paquete público de
// npm está descontinuado y arrastra avisos de seguridad— y ExcelJS, que pesa más que todo el módulo
// de reportes junto para usar el 2 % de su superficie. Lo que aquí hace falta es una hoja, una fila
// de encabezado y celdas tipadas; eso cabe en un archivo que se puede leer entero y afirmar en un
// test.
//
// Por qué no basta el CSV: un CSV entrega TEXTO. «3,5 h» y «05/08/2026» llegan a Excel como cadenas,
// así que el usuario no puede sumar, promediar ni ordenar por fecha sin limpiar el archivo a mano —
// que es exactamente el trabajo que un informe existe para evitar. Aquí los números viajan como
// números y las fechas como fechas.
//
// Un .xlsx es un zip de XML. Se escribe sin comprimir (método STORE): el ahorro de deflate no
// justifica meter un compresor, y Excel abre igual el archivo.

// ── Modelo ─────────────────────────────────────────────────────────────────────

/** Fecha con la precisión con que debe mostrarse: el día solo, o el día con la hora. */
export interface XlsxDate {
  /** Componentes de reloj de pared YA en el huso en que deben leerse. Ver `bogotaClock`. */
  year: number;
  month: number;
  day: number;
  hour?: number;
  minute?: number;
}

export type XlsxCell = string | number | XlsxDate | null;

export interface XlsxColumn {
  header: string;
  /** Ancho en caracteres. Sin esto Excel abre todo a 8,43 y las fechas salen como `#####`. */
  width?: number;
}

export interface XlsxSheet {
  name: string;
  columns: XlsxColumn[];
  rows: XlsxCell[][];
}

function isDate(value: XlsxCell): value is XlsxDate {
  return value !== null && typeof value === "object" && "year" in value;
}

/**
 * Componentes de reloj de pared en Bogotá para un instante ISO.
 *
 * Excel no guarda husos: un número de serie es una hora local sin más. Si se serializara el instante
 * UTC, un trámite decidido a las 22:00 de Bogotá aparecería en Excel al día siguiente — y el informe
 * en pantalla, que sí usa Bogotá, diría otra cosa. Las dos vistas tienen que coincidir.
 */
export function bogotaClock(iso: string | null): XlsxDate | null {
  if (!iso) return null;
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return null;

  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone: "America/Bogota",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).formatToParts(date);

  const get = (type: string) => Number(parts.find((p) => p.type === type)?.value ?? "0");
  // `hour12: false` produce «24» a medianoche en algunos motores; 24:00 del día D es 00:00 del D.
  const hour = get("hour") % 24;
  return { year: get("year"), month: get("month"), day: get("day"), hour, minute: get("minute") };
}

/**
 * El día en Bogotá, sin hora.
 *
 * Se distingue de `bogotaClock` porque el formato de la celda depende de ello: una fecha de
 * radicación mostrada como «01/08/2026 09:00» sugiere una precisión que el informe no usa —agrupa
 * por día calendario— y ensucia la columna al ordenarla de un vistazo.
 */
export function bogotaDay(iso: string | null): XlsxDate | null {
  const clock = bogotaClock(iso);
  if (!clock) return null;
  return { year: clock.year, month: clock.month, day: clock.day };
}

/** Número de serie de Excel: días desde el 30/12/1899, con la hora como fracción. */
function serialOf(value: XlsxDate): number {
  const EPOCH = Date.UTC(1899, 11, 30);
  const days = (Date.UTC(value.year, value.month - 1, value.day) - EPOCH) / 86_400_000;
  const seconds = (value.hour ?? 0) * 3600 + (value.minute ?? 0) * 60;
  return days + seconds / 86_400;
}

// ── XML ────────────────────────────────────────────────────────────────────────

function esc(value: string): string {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

/**
 * Los caracteres de control son ilegales en XML 1.0 y Excel rechaza el archivo entero —no la celda—
 * si aparece uno. Los valores de este informe vienen de campos que escribe la empresa cliente, así
 * que llegan tal cual desde una base de datos.
 */
const CONTROL_CHARS = /[\u0000-\u0008\u000B\u000C\u000E-\u001F]/g;

function sanitize(value: string): string {
  return value.replace(CONTROL_CHARS, "");
}

/** Referencia de columna: 0 → A, 25 → Z, 26 → AA. */
export function columnName(index: number): string {
  let name = "";
  let n = index;
  for (;;) {
    name = String.fromCharCode(65 + (n % 26)) + name;
    if (n < 26) return name;
    n = Math.floor(n / 26) - 1;
  }
}

/** Índices de `cellXfs` en styles.xml. El orden aquí y el de ese XML tienen que coincidir. */
const STYLE = { normal: 0, header: 1, fecha: 2, fechaHora: 3, decimal: 4 } as const;

function cellXml(ref: string, value: XlsxCell, header: boolean): string {
  if (header && typeof value === "string") {
    return `<c r="${ref}" s="${STYLE.header}" t="inlineStr"><is><t xml:space="preserve">${esc(sanitize(value))}</t></is></c>`;
  }
  if (value === null || value === "") return `<c r="${ref}"/>`;
  if (typeof value === "number") {
    // Los enteros van sin formato: «12,00 devoluciones» pide leer dos decimales que no existen.
    const style = Number.isInteger(value) ? STYLE.normal : STYLE.decimal;
    return `<c r="${ref}" s="${style}"><v>${value}</v></c>`;
  }
  if (isDate(value)) {
    const conHora = value.hour !== undefined;
    const style = conHora ? STYLE.fechaHora : STYLE.fecha;
    return `<c r="${ref}" s="${style}"><v>${serialOf(value)}</v></c>`;
  }
  return `<c r="${ref}" t="inlineStr"><is><t xml:space="preserve">${esc(sanitize(value))}</t></is></c>`;
}

function sheetXml(sheet: XlsxSheet): string {
  const lastCol = columnName(Math.max(0, sheet.columns.length - 1));
  const lastRow = sheet.rows.length + 1;

  const cols = sheet.columns
    .map((c, i) => `<col min="${i + 1}" max="${i + 1}" width="${c.width ?? 16}" customWidth="1"/>`)
    .join("");

  const header = `<row r="1">${sheet.columns
    .map((c, i) => cellXml(`${columnName(i)}1`, c.header, true))
    .join("")}</row>`;

  const body = sheet.rows
    .map((row, r) => {
      const cells = sheet.columns
        .map((_, i) => cellXml(`${columnName(i)}${r + 2}`, row[i] ?? null, false))
        .join("");
      return `<row r="${r + 2}">${cells}</row>`;
    })
    .join("");

  // Panel congelado y autofiltro: un informe de cientos de filas sin encabezado fijo obliga a
  // subir cada vez para recordar qué columna se está mirando.
  return `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><cols>${cols}</cols><sheetData>${header}${body}</sheetData><autoFilter ref="A1:${lastCol}${lastRow}"/></worksheet>`;
}

const STYLES_XML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="3"><numFmt numFmtId="164" formatCode="dd/mm/yyyy"/><numFmt numFmtId="165" formatCode="dd/mm/yyyy\\ hh:mm"/><numFmt numFmtId="166" formatCode="#,##0.00"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF162744"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="5"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="166" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs></styleSheet>`;

const CONTENT_TYPES_XML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>`;

const ROOT_RELS_XML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>`;

const WORKBOOK_RELS_XML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>`;

/** El nombre de hoja de Excel no admite `[]:*?/\` y se corta en 31 caracteres. */
function safeSheetName(name: string): string {
  const clean = name.replace(/[[\]:*?/\\]/g, " ").trim();
  return (clean || "Hoja1").slice(0, 31);
}

function workbookXml(sheetName: string): string {
  return `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="${esc(safeSheetName(sheetName))}" sheetId="1" r:id="rId1"/></sheets></workbook>`;
}

// ── ZIP ────────────────────────────────────────────────────────────────────────

const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let i = 0; i < 256; i += 1) {
    let c = i;
    for (let k = 0; k < 8; k += 1) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[i] = c >>> 0;
  }
  return table;
})();

function crc32(bytes: Uint8Array): number {
  let crc = 0xffffffff;
  for (let i = 0; i < bytes.length; i += 1) {
    crc = CRC_TABLE[(crc ^ bytes[i]) & 0xff]! ^ (crc >>> 8);
  }
  return (crc ^ 0xffffffff) >>> 0;
}

/**
 * Marca de tiempo DOS fija (01/01/2020 00:00).
 *
 * Deliberadamente constante: con la hora real, dos exportaciones del mismo informe producirían
 * archivos distintos byte a byte y ningún test podría afirmar sobre el resultado. La fecha del
 * archivo la pone el sistema de ficheros al descargarlo, que es donde el usuario la mira.
 */
const DOS_TIME = 0;
const DOS_DATE = ((2020 - 1980) << 9) | (1 << 5) | 1;

interface ZipEntry {
  name: string;
  bytes: Uint8Array;
  crc: number;
  offset: number;
}

class ByteWriter {
  private chunks: Uint8Array[] = [];
  length = 0;

  push(bytes: Uint8Array): void {
    this.chunks.push(bytes);
    this.length += bytes.length;
  }

  u16(value: number): void {
    this.push(new Uint8Array([value & 0xff, (value >>> 8) & 0xff]));
  }

  u32(value: number): void {
    this.push(
      new Uint8Array([
        value & 0xff,
        (value >>> 8) & 0xff,
        (value >>> 16) & 0xff,
        (value >>> 24) & 0xff,
      ]),
    );
  }

  toUint8Array(): Uint8Array<ArrayBuffer> {
    const out = new Uint8Array(this.length);
    let at = 0;
    for (const chunk of this.chunks) {
      out.set(chunk, at);
      at += chunk.length;
    }
    return out;
  }
}

function zip(files: { name: string; content: string }[]): Uint8Array<ArrayBuffer> {
  const encoder = new TextEncoder();
  const writer = new ByteWriter();
  const entries: ZipEntry[] = [];

  for (const file of files) {
    const bytes = encoder.encode(file.content);
    const nameBytes = encoder.encode(file.name);
    const entry: ZipEntry = {
      name: file.name,
      bytes,
      crc: crc32(bytes),
      offset: writer.length,
    };
    entries.push(entry);

    writer.u32(0x04034b50); // firma de cabecera local
    writer.u16(20); // versión necesaria
    writer.u16(0); // banderas
    writer.u16(0); // método: STORE
    writer.u16(DOS_TIME);
    writer.u16(DOS_DATE);
    writer.u32(entry.crc);
    writer.u32(bytes.length); // comprimido == sin comprimir
    writer.u32(bytes.length);
    writer.u16(nameBytes.length);
    writer.u16(0); // extra
    writer.push(nameBytes);
    writer.push(bytes);
  }

  const centralStart = writer.length;
  for (const entry of entries) {
    const nameBytes = encoder.encode(entry.name);
    writer.u32(0x02014b50); // firma de directorio central
    writer.u16(20); // versión creadora
    writer.u16(20); // versión necesaria
    writer.u16(0);
    writer.u16(0);
    writer.u16(DOS_TIME);
    writer.u16(DOS_DATE);
    writer.u32(entry.crc);
    writer.u32(entry.bytes.length);
    writer.u32(entry.bytes.length);
    writer.u16(nameBytes.length);
    writer.u16(0); // extra
    writer.u16(0); // comentario
    writer.u16(0); // disco
    writer.u16(0); // atributos internos
    writer.u32(0); // atributos externos
    writer.u32(entry.offset);
    writer.push(nameBytes);
  }

  const centralSize = writer.length - centralStart;
  writer.u32(0x06054b50); // fin del directorio central
  writer.u16(0);
  writer.u16(0);
  writer.u16(entries.length);
  writer.u16(entries.length);
  writer.u32(centralSize);
  writer.u32(centralStart);
  writer.u16(0); // comentario

  return writer.toUint8Array();
}

// ── API ────────────────────────────────────────────────────────────────────────

export const XLSX_MIME =
  "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

/** Libro de una hoja, listo para descargar. */
export function buildXlsx(sheet: XlsxSheet): Uint8Array<ArrayBuffer> {
  return zip([
    { name: "[Content_Types].xml", content: CONTENT_TYPES_XML },
    { name: "_rels/.rels", content: ROOT_RELS_XML },
    { name: "xl/workbook.xml", content: workbookXml(sheet.name) },
    { name: "xl/_rels/workbook.xml.rels", content: WORKBOOK_RELS_XML },
    { name: "xl/styles.xml", content: STYLES_XML },
    { name: "xl/worksheets/sheet1.xml", content: sheetXml(sheet) },
  ]);
}
