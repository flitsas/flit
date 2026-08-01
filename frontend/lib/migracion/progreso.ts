// El progreso de una migración masiva, guardado en el navegador para sobrevivir a un F5.
//
// Se guarda en localStorage y NO en sessionStorage: quien opera cierra la pestaña, se va a comer y
// vuelve. sessionStorage se pierde al cerrarla.
//
// **Lo guardado es una CREENCIA, no un hecho.** Puede quedarse corto (una migración que terminó en
// el servidor después de que se cortara la conexión) o rancio (otra persona migró los mismos ids).
// Por eso la consola reconcilia contra `/api/migracion/estado` al cargar: la libreta del servidor
// manda siempre. Sin esa reconciliación, esto sería una fuente de datos falsos con apariencia de
// verdad — que es peor que no guardar nada.
import type { Instancia, MigracionRespuesta, TipoTramite } from "./types";

const CLAVE = "flit:migracion:progreso";

/** Sube al cambiar la forma de lo guardado. Un formato viejo se descarta en vez de reventar. */
const VERSION = 1;

export type EstadoFila =
  | "pendiente"
  | "en_curso"
  | "simulado"
  | "migrado"
  | "con_avisos"
  | "fallido"
  | "ya_estaba";

export interface FilaLote {
  tramite: TipoTramite;
  v1Id: number;
  /** Fila del archivo original: es como quien opera identifica lo que cargó. */
  fila: number;
  estado: EstadoFila;
  /** Resultado de la última ejecución. Ausente mientras no se haya intentado. */
  respuesta?: MigracionRespuesta;
  /** Mensaje del fallo cuando `estado` es `fallido`. */
  error?: string;
}

export interface Lote {
  version: number;
  /** Nombre del archivo que se cargó; solo para que quien vuelve reconozca qué estaba haciendo. */
  archivo: string;
  creadoEl: string;
  instancias: Instancia[];
  dryRun: boolean;
  filas: FilaLote[];
}

/**
 * Estados que NO hay que volver a encolar: ya se resolvieron.
 *
 * `simulado` NO está aquí, y es lo que hace que el flujo recomendado funcione. Una simulación no
 * escribe nada, así que darla por terminada dejaría el lote entero bloqueado: quien simula las
 * veinte filas —justo lo que la ayuda aconseja— se encontraría con que ya no puede migrarlas de
 * verdad sin descartar el lote y volver a cargar el archivo.
 */
export const ESTADOS_TERMINADOS: readonly EstadoFila[] = ["migrado", "con_avisos", "ya_estaba"];

export function estaTerminada(fila: FilaLote): boolean {
  return ESTADOS_TERMINADOS.includes(fila.estado);
}

export function guardarLote(lote: Lote): void {
  try {
    window.localStorage.setItem(CLAVE, JSON.stringify(lote));
  } catch {
    // Cuota llena o modo privado de Safari. Se pierde la persistencia, no el trabajo en curso: la
    // ejecución vive en memoria y la reconciliación contra el servidor sigue funcionando. No vale
    // la pena interrumpir a quien está migrando para contarle esto.
  }
}

export function borrarLote(): void {
  try {
    window.localStorage.removeItem(CLAVE);
  } catch {
    // Ver guardarLote.
  }
}

/**
 * Recupera el lote guardado, o `null` si no hay ninguno o si lo guardado no tiene la forma
 * esperada. Todo lo que llegue de localStorage se valida: puede venir de una versión anterior de
 * la consola, o de alguien que lo editó a mano desde las herramientas del navegador.
 */
export function cargarLote(): Lote | null {
  let crudo: string | null;
  try {
    crudo = window.localStorage.getItem(CLAVE);
  } catch {
    return null;
  }

  if (!crudo) {
    return null;
  }

  try {
    const dato = JSON.parse(crudo) as Partial<Lote>;

    if (dato.version !== VERSION || !Array.isArray(dato.filas)) {
      return null;
    }

    const filas = dato.filas.filter(esFilaValida);
    if (filas.length === 0) {
      return null;
    }

    return {
      version: VERSION,
      archivo: typeof dato.archivo === "string" ? dato.archivo : "(sin nombre)",
      creadoEl: typeof dato.creadoEl === "string" ? dato.creadoEl : new Date().toISOString(),
      instancias: Array.isArray(dato.instancias) ? (dato.instancias as Instancia[]) : [],
      dryRun: dato.dryRun === true,
      filas,
    };
  } catch {
    return null;
  }
}

function esFilaValida(fila: unknown): fila is FilaLote {
  if (typeof fila !== "object" || fila === null) {
    return false;
  }

  const f = fila as Partial<FilaLote>;
  return (
    (f.tramite === "transfer" || f.tramite === "registration") &&
    typeof f.v1Id === "number" &&
    Number.isSafeInteger(f.v1Id) &&
    typeof f.estado === "string"
  );
}

/** Crea un lote nuevo a partir de lo que se leyó del archivo. */
export function nuevoLote(
  archivo: string,
  filas: Array<{ fila: number; tramite: TipoTramite; v1Id: number }>,
  instancias: Instancia[],
  dryRun: boolean,
): Lote {
  return {
    version: VERSION,
    archivo,
    creadoEl: new Date().toISOString(),
    instancias,
    dryRun,
    filas: filas.map((f) => ({ ...f, estado: "pendiente" as const })),
  };
}

/**
 * Clasifica el resultado de UNA migración.
 *
 * `ya_estaba` se distingue de `migrado` mirando el bloque `yaMigrado`, que es la foto de ANTES de
 * esta petición: si venía poblado, esta ejecución no creó nada. Es la diferencia entre «migré
 * veinte» y «migré tres, diecisiete ya estaban», y quien reporta el avance de una ola necesita esa
 * distinción.
 */
export function clasificar(respuesta: MigracionRespuesta): EstadoFila {
  if (respuesta.conProblemas) {
    return "fallido";
  }

  // Antes que el dry-run: que el trámite ya esté en la libreta es un HECHO leído del servidor, no
  // algo que dependa de si esta ejecución escribía o no. Y es información valiosa de una
  // simulación — decir cuáles de los veinte ya estaban es media parte de para qué se simula.
  if (respuesta.yaMigrado) {
    return "ya_estaba";
  }

  // Una simulación no migró nada, y decir lo contrario sería mentir en la única pantalla donde
  // alguien va a comprobar qué se hizo.
  if (respuesta.origen.dryRun) {
    return "simulado";
  }

  const hayAvisos = respuesta.instancias.some((i) => i.avisos.length > 0);
  return hayAvisos ? "con_avisos" : "migrado";
}

export const ETIQUETA_ESTADO: Record<EstadoFila, string> = {
  pendiente: "Pendiente",
  en_curso: "Migrando…",
  simulado: "Simulado, sin migrar",
  migrado: "Migrado",
  con_avisos: "Migrado con avisos",
  fallido: "Falló",
  ya_estaba: "Ya estaba migrado",
};
