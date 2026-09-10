import { describe, expect, it } from "vitest";
import { ErrorXlsx, leerXlsx } from "@/lib/migracion/xlsx";
import { validarHoja } from "@/lib/migracion/archivo";

/**
 * El lector de .xlsx se escribió a mano para no meter una dependencia de Excel en el frontend, así
 * que le toca demostrar que lee de verdad lo que Excel escribe: el ZIP se arma aquí byte a byte con
 * la misma estructura que produce una hoja de cálculo real.
 */

/** Comprime como lo hace Excel, para poder ejercitar el camino del método 8. */
async function deflactar(texto: string): Promise<Uint8Array> {
  const stream = new ReadableStream<BufferSource>({
    start(controller) {
      controller.enqueue(new TextEncoder().encode(texto));
      controller.close();
    },
  }).pipeThrough(new CompressionStream("deflate-raw"));

  return new Uint8Array(await new Response(stream).arrayBuffer());
}

/** Arma un ZIP con entradas SIN comprimir (método 0), que es válido y evita depender de zlib. */
function zip(archivos: Record<string, string>): ArrayBuffer {
  const codificador = new TextEncoder();
  return zipCrudo(
    new Map(
      Object.entries(archivos).map(([nombre, contenido]) => [
        nombre,
        { datos: codificador.encode(contenido), metodo: 0 },
      ]),
    ),
  );
}

/** El armador de verdad: acepta el método de compresión de cada entrada. */
function zipCrudo(archivos: Map<string, { datos: Uint8Array; metodo: number }>): ArrayBuffer {
  const codificador = new TextEncoder();
  const locales: Uint8Array[] = [];
  const centrales: Uint8Array[] = [];
  let offset = 0;

  for (const [nombre, { datos, metodo }] of archivos) {
    const bytesNombre = codificador.encode(nombre);

    const local = new Uint8Array(30 + bytesNombre.length + datos.length);
    const vl = new DataView(local.buffer);
    vl.setUint32(0, 0x0403_4b50, true);
    vl.setUint16(8, metodo, true);
    vl.setUint32(18, datos.length, true); // tamaño comprimido
    vl.setUint32(22, datos.length, true); // tamaño sin comprimir
    vl.setUint16(26, bytesNombre.length, true);
    local.set(bytesNombre, 30);
    local.set(datos, 30 + bytesNombre.length);

    const central = new Uint8Array(46 + bytesNombre.length);
    const vc = new DataView(central.buffer);
    vc.setUint32(0, 0x0201_4b50, true);
    vc.setUint16(10, metodo, true);
    vc.setUint32(20, datos.length, true);
    vc.setUint32(24, datos.length, true);
    vc.setUint16(28, bytesNombre.length, true);
    vc.setUint32(42, offset, true);
    central.set(bytesNombre, 46);

    locales.push(local);
    centrales.push(central);
    offset += local.length;
  }

  const inicioCentral = offset;
  const tamCentral = centrales.reduce((n, c) => n + c.length, 0);

  const fin = new Uint8Array(22);
  const vf = new DataView(fin.buffer);
  vf.setUint32(0, 0x0605_4b50, true);
  vf.setUint16(8, centrales.length, true);
  vf.setUint16(10, centrales.length, true);
  vf.setUint32(12, tamCentral, true);
  vf.setUint32(16, inicioCentral, true);

  const total = [...locales, ...centrales, fin];
  const salida = new Uint8Array(total.reduce((n, p) => n + p.length, 0));
  let cursor = 0;
  for (const parte of total) {
    salida.set(parte, cursor);
    cursor += parte.length;
  }

  return salida.buffer;
}

const WORKBOOK = `<?xml version="1.0"?><workbook xmlns:r="x"><sheets><sheet name="Hoja1" sheetId="1" r:id="rId1"/></sheets></workbook>`;
const RELS = `<?xml version="1.0"?><Relationships><Relationship Id="rId1" Target="worksheets/sheet1.xml"/></Relationships>`;

function libro(hoja: string, compartidas?: string): ArrayBuffer {
  const archivos: Record<string, string> = {
    "xl/workbook.xml": WORKBOOK,
    "xl/_rels/workbook.xml.rels": RELS,
    "xl/worksheets/sheet1.xml": hoja,
  };
  if (compartidas) {
    archivos["xl/sharedStrings.xml"] = compartidas;
  }
  return zip(archivos);
}

describe("leerXlsx", () => {
  it("resuelve las cadenas compartidas y los números en línea", async () => {
    const compartidas = `<sst><si><t>tipo</t></si><si><t>id</t></si><si><t>traspaso</t></si></sst>`;
    const hoja = `<worksheet><sheetData>
      <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
      <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2"><v>26350</v></c></row>
    </sheetData></worksheet>`;

    expect(await leerXlsx(libro(hoja, compartidas))).toEqual([
      ["tipo", "id"],
      ["traspaso", "26350"],
    ]);
  });

  /**
   * El caso que obliga a mirar la referencia de celda: Excel OMITE las celdas vacías. Sin leer el
   * "r", una fila cuya primera celda está en blanco correría el id a la columna del tipo.
   */
  it("respeta la columna real cuando faltan celdas", async () => {
    const hoja = `<worksheet><sheetData>
      <row r="1"><c r="B1"><v>26350</v></c></row>
    </sheetData></worksheet>`;

    expect(await leerXlsx(libro(hoja))).toEqual([["", "26350"]]);
  });

  it("lee el texto en línea", async () => {
    const hoja = `<worksheet><sheetData>
      <row r="1"><c r="A1" t="inlineStr"><is><t>traspaso</t></is></c></row>
    </sheetData></worksheet>`;

    expect(await leerXlsx(libro(hoja))).toEqual([["traspaso"]]);
  });

  /** Las hojas se pueden reordenar; el nombre de archivo no cambia y solo la relación manda. */
  it("sigue la relación r:id en vez de asumir sheet1.xml", async () => {
    const rels = `<?xml version="1.0"?><Relationships><Relationship Id="rId1" Target="worksheets/sheet7.xml"/></Relationships>`;
    const datos = zip({
      "xl/workbook.xml": WORKBOOK,
      "xl/_rels/workbook.xml.rels": rels,
      "xl/worksheets/sheet1.xml": `<worksheet><sheetData><row><c r="A1" t="inlineStr"><is><t>otra</t></is></c></row></sheetData></worksheet>`,
      "xl/worksheets/sheet7.xml": `<worksheet><sheetData><row><c r="A1" t="inlineStr"><is><t>buena</t></is></c></row></sheetData></worksheet>`,
    });

    expect(await leerXlsx(datos)).toEqual([["buena"]]);
  });

  /**
   * Los tests de arriba usan entradas ALMACENADAS (método 0) porque el ZIP se arma a mano. Excel
   * escribe DESINFLADO (método 8), que es el camino que de verdad se recorre en producción y el
   * único que usa `DecompressionStream`. Sin este caso, el lector podría estar roto justo ahí y
   * todo lo demás seguiría en verde.
   */
  it("infla las entradas comprimidas, que es lo que escribe Excel", async () => {
    const xml = `<worksheet><sheetData><row><c r="A1" t="inlineStr"><is><t>traspaso</t></is></c><c r="B1"><v>26350</v></c></row></sheetData></worksheet>`;

    const datos = await deflactar(xml);
    const archivos = new Map<string, { datos: Uint8Array; metodo: number }>([
      ["xl/workbook.xml", { datos: new TextEncoder().encode(WORKBOOK), metodo: 0 }],
      ["xl/_rels/workbook.xml.rels", { datos: new TextEncoder().encode(RELS), metodo: 0 }],
      ["xl/worksheets/sheet1.xml", { datos, metodo: 8 }],
    ]);

    expect(await leerXlsx(zipCrudo(archivos))).toEqual([["traspaso", "26350"]]);
  });

  it("rechaza un archivo que no es un ZIP", async () => {
    const basura = new TextEncoder().encode("esto es un PDF, no un Excel").buffer;

    await expect(leerXlsx(basura)).rejects.toBeInstanceOf(ErrorXlsx);
  });

  it("rechaza un ZIP que no es un libro de Excel", async () => {
    await expect(leerXlsx(zip({ "hola.txt": "mundo" }))).rejects.toBeInstanceOf(ErrorXlsx);
  });

  /** El camino real de punta a punta: un .xlsx entra y salen filas listas para migrar. */
  it("encaja con la validación de filas", async () => {
    const compartidas = `<sst><si><t>tipo</t></si><si><t>id</t></si><si><t>traspaso</t></si><si><t>matricula</t></si></sst>`;
    const hoja = `<worksheet><sheetData>
      <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
      <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2"><v>26350</v></c></row>
      <row r="3"><c r="A3" t="s"><v>3</v></c><c r="B3"><v>7426</v></c></row>
    </sheetData></worksheet>`;

    const { validas, invalidas } = validarHoja(await leerXlsx(libro(hoja, compartidas)));

    expect(invalidas).toEqual([]);
    expect(validas).toEqual([
      { fila: 2, tramite: "transfer", v1Id: 26350 },
      { fila: 3, tramite: "registration", v1Id: 7426 },
    ]);
  });
});
