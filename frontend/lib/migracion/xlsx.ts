// Lector mínimo de .xlsx, sin dependencias.
//
// Por qué a mano y no con una librería: lo que hay que leer aquí es una tabla de dos columnas de
// texto plano —el tipo de trámite y un id—, y las librerías de Excel del registro npm traen un
// coste que no compensa para eso. SheetJS publica en npm una versión estancada con avisos de
// seguridad abiertos, y `exceljs` arrastra un árbol de dependencias grande para un frontend que hoy
// tiene NUEVE en total. La verificación de `pnpm audit --prod --audit-level high` es parte del CI, y
// añadir superficie de ataque a la consola que reescribe trámites de producción es exactamente
// donde no conviene.
//
// Lo que sí se usa es lo que el navegador ya trae: `DecompressionStream('deflate-raw')` para
// inflar, y `DOMParser` para el XML. Un .xlsx es un ZIP con XML dentro.
//
// ALCANCE DELIBERADO: se lee la PRIMERA hoja y se devuelven celdas como texto. No hay fórmulas,
// ni fechas, ni formatos, ni hojas múltiples. Si algún día hiciera falta algo de eso, la respuesta
// correcta es una librería, no hacer crecer este archivo.

/** Una hoja como matriz de filas × celdas de texto. Las celdas vacías son cadenas vacías. */
export type Hoja = string[][];

/** Falla con un mensaje legible: quien opera subió un archivo, no un ZIP a mano. */
export class ErrorXlsx extends Error {
  constructor(mensaje: string) {
    super(mensaje);
    this.name = "ErrorXlsx";
  }
}

export async function leerXlsx(datos: ArrayBuffer): Promise<Hoja> {
  const entradas = leerZip(new Uint8Array(datos));

  const workbook = entradas.get("xl/workbook.xml");
  if (!workbook) {
    throw new ErrorXlsx("El archivo no parece un Excel: no tiene xl/workbook.xml dentro.");
  }

  const rutaHoja = await resolverPrimeraHoja(entradas, workbook);
  const xmlHoja = entradas.get(rutaHoja);
  if (!xmlHoja) {
    throw new ErrorXlsx(`El Excel declara una hoja en ${rutaHoja} pero el archivo no la contiene.`);
  }

  const compartidas = await leerCadenasCompartidas(entradas);
  return parsearHoja(await inflar(xmlHoja), compartidas);
}

// ─────────────────────────────────────────────────────────────────── ZIP

interface EntradaZip {
  // El parámetro de tipo se fija a ArrayBuffer (y no al ArrayBufferLike por defecto) porque
  // DecompressionStream no acepta un búfer compartido; sin esto no compila.
  comprimido: Uint8Array<ArrayBuffer>;
  metodo: number;
}

/**
 * Se recorre el DIRECTORIO CENTRAL y no las cabeceras locales en secuencia.
 *
 * No es purismo: cuando el bit 3 de las banderas está puesto —lo que hacen los escritores que
 * generan el ZIP en streaming, y hay varios que exportan .xlsx así— la cabecera local trae los
 * tamaños en cero y los verdaderos van en un descriptor DESPUÉS de los datos. Leyendo en secuencia,
 * esos archivos se leen como vacíos. El directorio central siempre tiene los tamaños buenos.
 */
function leerZip(bytes: Uint8Array<ArrayBuffer>): Map<string, EntradaZip> {
  const vista = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const finDirectorio = buscarFinDelDirectorio(bytes, vista);

  const total = vista.getUint16(finDirectorio + 10, true);
  let cursor = vista.getUint32(finDirectorio + 16, true);

  const entradas = new Map<string, EntradaZip>();
  const decodificador = new TextDecoder();

  for (let i = 0; i < total; i++) {
    if (vista.getUint32(cursor, true) !== 0x0201_4b50) {
      throw new ErrorXlsx("El archivo está dañado: el directorio interno no cuadra.");
    }

    const metodo = vista.getUint16(cursor + 10, true);
    const tamComprimido = vista.getUint32(cursor + 20, true);
    const largoNombre = vista.getUint16(cursor + 28, true);
    const largoExtra = vista.getUint16(cursor + 30, true);
    const largoComentario = vista.getUint16(cursor + 32, true);
    const offsetLocal = vista.getUint32(cursor + 42, true);

    const nombre = decodificador.decode(bytes.subarray(cursor + 46, cursor + 46 + largoNombre));

    // Los largos de nombre y extra de la cabecera LOCAL pueden diferir de los del directorio
    // (el campo extra suele llevar relleno de alineación), así que se releen de la local.
    const largoNombreLocal = vista.getUint16(offsetLocal + 26, true);
    const largoExtraLocal = vista.getUint16(offsetLocal + 28, true);
    const inicioDatos = offsetLocal + 30 + largoNombreLocal + largoExtraLocal;

    entradas.set(nombre, {
      metodo,
      comprimido: bytes.subarray(inicioDatos, inicioDatos + tamComprimido),
    });

    cursor += 46 + largoNombre + largoExtra + largoComentario;
  }

  return entradas;
}

function buscarFinDelDirectorio(bytes: Uint8Array<ArrayBuffer>, vista: DataView): number {
  // El registro final mide 22 bytes más un comentario de hasta 64 kB, así que se busca hacia atrás.
  const minimo = Math.max(0, bytes.length - 22 - 0xffff);
  for (let i = bytes.length - 22; i >= minimo; i--) {
    if (vista.getUint32(i, true) === 0x0605_4b50) {
      return i;
    }
  }
  throw new ErrorXlsx("El archivo no es un .xlsx válido (no se encontró la estructura del ZIP).");
}

async function inflar({ comprimido, metodo }: EntradaZip): Promise<string> {
  // Método 0 = almacenado sin comprimir. Los archivos pequeños de un .xlsx a veces salen así.
  if (metodo === 0) {
    return new TextDecoder().decode(comprimido);
  }

  if (metodo !== 8) {
    throw new ErrorXlsx(`El Excel usa un método de compresión no soportado (${metodo}).`);
  }

  // El flujo se arma a mano en vez de con `new Blob([...]).stream()`: `Blob.stream` no existe en
  // todos los entornos donde corre este código (jsdom, entre otros), y `ReadableStream` sí.
  //
  // `deflate-raw` y no `deflate`: dentro de un ZIP los datos van sin la cabecera zlib.
  const stream = new ReadableStream<BufferSource>({
    start(controller) {
      controller.enqueue(comprimido);
      controller.close();
    },
  }).pipeThrough(new DecompressionStream("deflate-raw"));

  return new Response(stream).text();
}

// ─────────────────────────────────────────────────────────────────── XML

function parsearXml(xml: string, queEs: string): Document {
  const doc = new DOMParser().parseFromString(xml, "application/xml");
  if (doc.querySelector("parsererror")) {
    throw new ErrorXlsx(`El Excel trae un ${queEs} ilegible.`);
  }
  return doc;
}

/**
 * La primera hoja del LIBRO, que no siempre es `sheet1.xml`: al reordenar o borrar hojas en Excel,
 * los nombres de archivo se quedan como estaban. El orden bueno es el de `<sheets>` en workbook.xml,
 * y el nombre de archivo sale de la relación `r:id`.
 */
async function resolverPrimeraHoja(
  entradas: Map<string, EntradaZip>,
  workbook: EntradaZip,
): Promise<string> {
  const doc = parsearXml(await inflar(workbook), "índice de hojas");
  const primera = doc.getElementsByTagName("sheet")[0];
  const relId = primera?.getAttribute("r:id") ?? primera?.getAttribute("id");

  const rels = entradas.get("xl/_rels/workbook.xml.rels");
  if (relId && rels) {
    const relDoc = parsearXml(await inflar(rels), "mapa de relaciones");
    for (const rel of Array.from(relDoc.getElementsByTagName("Relationship"))) {
      if (rel.getAttribute("Id") === relId) {
        const destino = rel.getAttribute("Target") ?? "";
        // Los destinos son relativos a xl/ y a veces vienen con "/xl/" absoluto.
        return destino.startsWith("/")
          ? destino.slice(1)
          : `xl/${destino.replace(/^\.\//, "")}`;
      }
    }
  }

  return "xl/worksheets/sheet1.xml";
}

/**
 * La tabla de cadenas: Excel no guarda el texto en la celda sino un índice a esta lista. Un libro
 * de solo números no la trae, y eso no es un error.
 */
async function leerCadenasCompartidas(entradas: Map<string, EntradaZip>): Promise<string[]> {
  const archivo = entradas.get("xl/sharedStrings.xml");
  if (!archivo) {
    return [];
  }

  const doc = parsearXml(await inflar(archivo), "diccionario de textos");
  return Array.from(doc.getElementsByTagName("si")).map(textoDeNodo);
}

/**
 * El texto de un nodo saltándose los `<rPh>` (guías fonéticas del japonés), que `textContent`
 * concatenaría al valor real.
 */
function textoDeNodo(nodo: Element): string {
  return Array.from(nodo.getElementsByTagName("t"))
    .filter((t) => t.parentElement?.tagName !== "rPh")
    .map((t) => t.textContent ?? "")
    .join("");
}

function parsearHoja(xml: string, compartidas: string[]): Hoja {
  const doc = parsearXml(xml, "contenido de hoja");
  const filas: Hoja = [];

  for (const fila of Array.from(doc.getElementsByTagName("row"))) {
    const celdas: string[] = [];

    for (const celda of Array.from(fila.getElementsByTagName("c"))) {
      // La referencia (A1, C7…) da la COLUMNA real: Excel omite las celdas vacías, así que sin
      // esto una fila con la primera celda en blanco correría todo un puesto a la izquierda.
      const columna = indiceDeColumna(celda.getAttribute("r") ?? "");
      const destino = columna >= 0 ? columna : celdas.length;

      while (celdas.length <= destino) {
        celdas.push("");
      }

      celdas[destino] = valorDeCelda(celda, compartidas);
    }

    filas.push(celdas);
  }

  return filas;
}

function valorDeCelda(celda: Element, compartidas: string[]): string {
  const tipo = celda.getAttribute("t");

  if (tipo === "s") {
    const indice = Number.parseInt(celda.getElementsByTagName("v")[0]?.textContent ?? "", 10);
    return Number.isInteger(indice) ? (compartidas[indice] ?? "") : "";
  }

  if (tipo === "inlineStr") {
    return textoDeNodo(celda);
  }

  return celda.getElementsByTagName("v")[0]?.textContent?.trim() ?? "";
}

/** "B7" → 1. Devuelve -1 si la referencia no trae letras de columna. */
function indiceDeColumna(referencia: string): number {
  const letras = /^([A-Z]+)/.exec(referencia)?.[1];
  if (!letras) {
    return -1;
  }

  let indice = 0;
  for (const letra of letras) {
    indice = indice * 26 + (letra.charCodeAt(0) - 64);
  }
  return indice - 1;
}
