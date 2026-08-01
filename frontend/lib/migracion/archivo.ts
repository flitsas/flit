// Cargue masivo: plantilla, lectura de CSV/Excel y validación de lo que trae el archivo.
//
// La validación devuelve SIEMPRE las filas buenas y las malas por separado, nunca un "está mal" a
// secas. Un archivo de veinte trámites con una celda torcida no debería obligar a rehacerlo entero:
// se migran las diecinueve buenas y se dice exactamente qué fila corregir.
import type { TipoTramite } from "./types";
import { leerXlsx, type Hoja } from "./xlsx";

/** Una fila que sí se puede migrar. */
export interface FilaValida {
  /** Número de fila del archivo tal y como lo ve quien opera (con encabezado, base 1). */
  fila: number;
  tramite: TipoTramite;
  v1Id: number;
}

/** Una fila que no se puede migrar, con el motivo en el idioma de quien la escribió. */
export interface FilaInvalida {
  fila: number;
  contenido: string;
  motivo: string;
}

export interface LecturaArchivo {
  validas: FilaValida[];
  invalidas: FilaInvalida[];
}

/**
 * Sinónimos aceptados para el tipo de trámite. La plantilla dice `traspaso`/`matricula`, pero quien
 * ya usó el endpoint por Postman escribirá `transfer`/`registration`, y quien copie de una consulta
 * SQL traerá acentos y mayúsculas. Todos son la misma intención y ninguno merece un error.
 */
const SINONIMOS: Record<string, TipoTramite> = {
  traspaso: "transfer",
  transfer: "transfer",
  traspasos: "transfer",
  matricula: "registration",
  matriculas: "registration",
  registration: "registration",
  matriculainicial: "registration",
  matriculanueva: "registration",
};

/** Encabezados que se reconocen como la columna del tipo y la del id. */
const ENCABEZADOS_TIPO = ["tipo", "tramite", "tipotramite", "tipodetramite"];
const ENCABEZADOS_ID = ["id", "v1id", "idv1", "iddev1", "idtramite"];

export const NOMBRE_PLANTILLA = "plantilla-migracion.csv";

/**
 * La plantilla que se descarga. Va con ejemplos reales comentados con `#` para que se vea el
 * formato sin que las filas de muestra se migren por accidente si alguien no las borra.
 */
export function contenidoPlantilla(): string {
  return [
    "tipo,id",
    "# Una fila por trámite. 'tipo' acepta traspaso o matricula; 'id' es el id de V1.",
    "# Borra estas líneas de ejemplo antes de cargar el archivo.",
    "traspaso,26350",
    "matricula,7426",
  ].join("\r\n");
}

/**
 * Lee el archivo que subieron. Decide por extensión y no por el tipo MIME del navegador, que en
 * Windows llega vacío o como `application/octet-stream` según qué haya instalado.
 */
export async function leerArchivo(archivo: File): Promise<LecturaArchivo> {
  const nombre = archivo.name.toLowerCase();

  if (nombre.endsWith(".xlsx")) {
    return validarHoja(await leerXlsx(await archivo.arrayBuffer()));
  }

  if (nombre.endsWith(".csv") || nombre.endsWith(".txt")) {
    return validarHoja(parsearCsv(await archivo.text()));
  }

  if (nombre.endsWith(".xls")) {
    throw new Error(
      "El formato .xls (Excel 97-2003) no se soporta. Vuelve a guardarlo como .xlsx o como CSV.",
    );
  }

  throw new Error(`No se reconoce la extensión de «${archivo.name}». Usa .csv o .xlsx.`);
}

/**
 * CSV con comillas, según RFC 4180: una comilla doble dentro de un campo entrecomillado se escribe
 * duplicada. Se implementa a mano porque `split(",")` se rompe con el primer campo que traiga una
 * coma, y ese es el tipo de fallo que corrompe datos en silencio.
 *
 * Acepta `;` como separador además de `,`: es lo que produce Excel en español al «Guardar como CSV».
 */
export function parsearCsv(texto: string): Hoja {
  // El BOM que antepone Excel se colaría en el primer encabezado y lo haría irreconocible.
  const limpio = texto.replace(/^\ufeff/, "");
  const separador = detectarSeparador(limpio);

  const filas: Hoja = [];
  let celdas: string[] = [];
  let celda = "";
  let entreComillas = false;

  for (let i = 0; i < limpio.length; i++) {
    const c = limpio[i];

    if (entreComillas) {
      if (c === '"') {
        if (limpio[i + 1] === '"') {
          celda += '"';
          i++;
        } else {
          entreComillas = false;
        }
      } else {
        celda += c;
      }
      continue;
    }

    if (c === '"') {
      entreComillas = true;
    } else if (c === separador) {
      celdas.push(celda);
      celda = "";
    } else if (c === "\n") {
      celdas.push(celda);
      filas.push(celdas);
      celdas = [];
      celda = "";
    } else if (c !== "\r") {
      celda += c;
    }
  }

  if (celda !== "" || celdas.length > 0) {
    celdas.push(celda);
    filas.push(celdas);
  }

  return filas;
}

function detectarSeparador(texto: string): string {
  const primeraLinea = texto.slice(0, texto.indexOf("\n") + 1 || texto.length);
  return primeraLinea.split(";").length > primeraLinea.split(",").length ? ";" : ",";
}

/** Normaliza para comparar: sin acentos, sin espacios, en minúsculas. */
function normalizar(valor: string): string {
  return valor
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]/g, "");
}

export function validarHoja(hoja: Hoja): LecturaArchivo {
  const validas: FilaValida[] = [];
  const invalidas: FilaInvalida[] = [];

  const { columnaTipo, columnaId, filaInicial } = ubicarColumnas(hoja);
  const yaVistos = new Map<string, number>();

  for (let i = filaInicial; i < hoja.length; i++) {
    const celdas = hoja[i];
    const numeroFila = i + 1;
    const contenido = celdas.join(", ").trim();

    // Filas en blanco y comentarios: se ignoran en silencio. Las hojas de cálculo dejan filas
    // vacías al final constantemente, y reportarlas como errores sería ruido puro.
    if (contenido === "" || contenido.startsWith("#")) {
      continue;
    }

    const crudoTipo = (celdas[columnaTipo] ?? "").trim();
    const crudoId = (celdas[columnaId] ?? "").trim();

    const tramite = SINONIMOS[normalizar(crudoTipo)];
    if (!tramite) {
      invalidas.push({
        fila: numeroFila,
        contenido,
        motivo: crudoTipo
          ? `«${crudoTipo}» no es un tipo de trámite. Usa traspaso o matricula.`
          : "Falta el tipo de trámite (traspaso o matricula).",
      });
      continue;
    }

    const v1Id = parsearId(crudoId);
    if (v1Id === null) {
      invalidas.push({
        fila: numeroFila,
        contenido,
        motivo: crudoId
          ? `«${crudoId}» no es un id de V1: se espera un número entero positivo.`
          : "Falta el id de V1.",
      });
      continue;
    }

    // Duplicados: migrar dos veces el mismo id es inofensivo (el migrador responde "ya migrado"),
    // pero deja a quien opera contando filas que nunca cuadran. Se señala y no se encola.
    const clave = `${tramite}:${v1Id}`;
    const anterior = yaVistos.get(clave);
    if (anterior !== undefined) {
      invalidas.push({
        fila: numeroFila,
        contenido,
        motivo: `Repetido: este mismo trámite ya venía en la fila ${anterior}.`,
      });
      continue;
    }

    yaVistos.set(clave, numeroFila);
    validas.push({ fila: numeroFila, tramite, v1Id });
  }

  return { validas, invalidas };
}

/**
 * Ubica las dos columnas por su encabezado. Si no hay encabezado reconocible se asume el orden de
 * la plantilla (tipo, id) y se lee desde la primera fila: quien borró la cabecera al copiar y pegar
 * no debería quedarse sin poder cargar.
 */
function ubicarColumnas(hoja: Hoja): {
  columnaTipo: number;
  columnaId: number;
  filaInicial: number;
} {
  const cabecera = (hoja[0] ?? []).map(normalizar);
  const columnaTipo = cabecera.findIndex((c) => ENCABEZADOS_TIPO.includes(c));
  const columnaId = cabecera.findIndex((c) => ENCABEZADOS_ID.includes(c));

  if (columnaTipo >= 0 && columnaId >= 0) {
    return { columnaTipo, columnaId, filaInicial: 1 };
  }

  return { columnaTipo: 0, columnaId: 1, filaInicial: 0 };
}

/**
 * Un id de V1 tal y como puede haber quedado tras pasar por una hoja de cálculo.
 *
 * Las tres formas se aceptan por separado en vez de "quitar todo lo que no sea dígito": esa versión
 * perezosa convertiría `26350-1` en `263501` y migraría un trámite que nadie pidió. Lo que no encaje
 * en un patrón conocido se rechaza y se reporta.
 */
function parsearId(crudo: string): number | null {
  const sinEspacios = crudo.replace(/\s/g, "");

  let digitos: string | null = null;

  if (/^\d+$/.test(sinEspacios)) {
    digitos = sinEspacios;
  } else if (/^\d+[.,]0+$/.test(sinEspacios)) {
    // "26350.0": Excel exportando a CSV una columna con formato numérico.
    digitos = sinEspacios.split(/[.,]/)[0];
  } else if (/^\d{1,3}([.,]\d{3})+$/.test(sinEspacios)) {
    // "26.350": separador de miles del formato de celda.
    digitos = sinEspacios.replace(/[.,]/g, "");
  }

  if (digitos === null) {
    return null;
  }

  const valor = Number.parseInt(digitos, 10);
  return Number.isSafeInteger(valor) && valor > 0 ? valor : null;
}

/** Dispara la descarga de la plantilla sin pedirle nada al servidor. */
export function descargarPlantilla(): void {
  // El BOM es lo que hace que Excel abra el CSV en UTF-8 en vez de en la codificación local.
  const blob = new Blob([`\ufeff${contenidoPlantilla()}`], {
    type: "text/csv;charset=utf-8",
  });
  const url = URL.createObjectURL(blob);

  const enlace = document.createElement("a");
  enlace.href = url;
  enlace.download = NOMBRE_PLANTILLA;
  enlace.click();

  URL.revokeObjectURL(url);
}
