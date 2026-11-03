// Declaraciones del tipo (color nuevo, combustible, carrocería, blindaje, causal de cancelación…). Módulo puro, sin React ni red.
// El back las describe en la definición (`declaraciones`) y aquí solo se decide qué se muestra, qué soportes pide cada opción, qué
// falta y qué valores se guardan. Ningún tipo de trámite está nombrado: todo sale de los metadatos de la declaración.
import type { Adjunto, DeclaracionTipo, DefinicionTipo, OpcionDeclaracion } from "@/lib/api/cliente";
import { adjuntoDe, MENSAJE_FALTA_ARCHIVO, MENSAJE_OBLIGATORIO } from "@/lib/tramite/requisitos";

/** Control que el renderer de declaraciones NO pinta: la secretaría de destino va en el diagnóstico (web) o en Requisitos (punto) con `OrganismoDestinoField`. */
export const CONTROL_DESTINO = "organismo_destino";

/**
 * Banderas `cambio_*` que acompañan a una declaración de transformación, y la transformación complementaria («trámite simultáneo»)
 * que las enciende. Son los mismos códigos que cotiza el portal en `TRANSFORMACIONES`.
 */
const TRANSFORMACION_POR_BANDERA: Readonly<Record<string, string>> = {
  cambio_color: "CAMBIO_COLOR",
  cambio_combustible: "CONVERSION_COMBUSTIBLE",
  cambio_carroceria: "CAMBIO_CARROCERIA",
};

/** Declaraciones que pinta el renderer único (todas menos la del destino). */
export const declaracionesDe = (d: DefinicionTipo): DeclaracionTipo[] => (d.declaraciones ?? []).filter((x) => x.control !== CONTROL_DESTINO);

/** La declaración de la secretaría de destino, si el back la trae. */
export const declaracionDestino = (d: DefinicionTipo): DeclaracionTipo | undefined => (d.declaraciones ?? []).find((x) => x.control === CONTROL_DESTINO);

/**
 * Transformación complementaria («trámite simultáneo») que activa la declaración: la que informa el back en `activadaPor`; si el
 * back no la informa (definición anterior), se deduce de las banderas `cambio_*` de una declaración opcional. `null` si no depende de ninguna.
 */
export function transformacionDe(decl: DeclaracionTipo): string | null {
  const informada = decl.activadaPor?.trim();
  if (informada) return informada.toUpperCase();
  if (decl.activadaPor !== undefined || decl.obligatoria) return null;
  for (const bandera of decl.fieldKeysExtra) {
    const codigo = TRANSFORMACION_POR_BANDERA[bandera];
    if (codigo) return codigo;
  }
  return null;
}

/**
 * Si la declaración se pide ahora. Una atada a un trámite simultáneo solo mientras ese trámite esté activado (y entonces se
 * completa); las demás siempre se ofrecen (obligatorias u opcionales).
 */
export function declaracionActiva(decl: DeclaracionTipo, transformaciones: readonly string[]): boolean {
  if (decl.control === CONTROL_DESTINO) return false;
  const atada = transformacionDe(decl);
  return atada === null || transformaciones.some((t) => t.toUpperCase() === atada);
}

export const declaracionesActivas = (d: DefinicionTipo, transformaciones: readonly string[]): DeclaracionTipo[] =>
  declaracionesDe(d).filter((x) => declaracionActiva(x, transformaciones));

/** Las opciones en línea de una declaración con la elegida (vacío si el valor viene de un catálogo). */
export const opcionElegida = (decl: DeclaracionTipo, valor: string): OpcionDeclaracion | undefined => decl.opciones.find((o) => o.codigo === valor);

export interface SoporteDeclaracion {
  docCode: string;
  nombre: string;
  obligatorio: boolean;
}

/** «soporte_cambio_color» → «Soporte cambio color»: nombre de respaldo cuando la matriz del tipo no trae el documento. */
export function nombreLegibleDeDocumento(docCode: string): string {
  const texto = docCode.replace(/_/g, " ").trim();
  return texto.charAt(0).toUpperCase() + texto.slice(1);
}

const nombreDelDocumento = (d: DefinicionTipo, docCode: string): string => d.documentos.find((x) => x.docCode === docCode)?.nombre || nombreLegibleDeDocumento(docCode);

/**
 * Soportes que pide el valor elegido. Si la opción trae `docCodes` (los de una causal), esos son los soportes y obligan según su
 * `exigeSoporte`. Si no los trae, el soporte es el general de la declaración (`docCodeSoporte`): obligatorio si la opción lo exige o
 * si la declaración lo marca así (`soporteObligatorio`), y opcional en otro caso (p. ej. el soporte de un cambio de combustible).
 * Con un valor de catálogo (sin opción en línea) manda el soporte general y su `soporteObligatorio`. Sin valor no se pide ninguno.
 */
export function soportesDe(decl: DeclaracionTipo, valor: string, d: DefinicionTipo): SoporteDeclaracion[] {
  if (valor.trim() === "") return [];
  const opcion = opcionElegida(decl, valor);
  let codigos: string[];
  let obligatorio: boolean;
  if (opcion) {
    const propios = opcion.docCodes.length > 0;
    codigos = propios ? opcion.docCodes : decl.docCodeSoporte ? [decl.docCodeSoporte] : [];
    obligatorio = opcion.exigeSoporte || (!propios && decl.soporteObligatorio);
  } else {
    codigos = decl.docCodeSoporte ? [decl.docCodeSoporte] : [];
    obligatorio = decl.soporteObligatorio;
  }
  return [...new Set(codigos)].map((docCode) => ({ docCode, nombre: nombreDelDocumento(d, docCode), obligatorio }));
}

/**
 * Errores de lo declarado, con las llaves de la pantalla: `declaracion.<clave>` (falta el valor) y `documento.<docCode>` (falta el
 * soporte que pide el valor elegido; mismo prefijo que los documentos de la matriz, así no frena el autoguardado).
 */
export function erroresDeDeclaraciones(
  d: DefinicionTipo,
  adjuntos: readonly Adjunto[],
  campos: Readonly<Record<string, string>>,
  transformaciones: readonly string[] = [],
): Record<string, string> {
  const errores: Record<string, string> = {};
  for (const decl of declaracionesActivas(d, transformaciones)) {
    const valor = (campos[decl.fieldKey] ?? "").trim();
    if (valor === "") {
      if (decl.obligatoria || transformacionDe(decl) !== null) errores[`declaracion.${decl.clave}`] = MENSAJE_OBLIGATORIO;
      continue;
    }
    if (decl.opciones.length > 0 && !opcionElegida(decl, valor)) {
      errores[`declaracion.${decl.clave}`] = "Elige una de las opciones";
      continue;
    }
    for (const soporte of soportesDe(decl, valor, d)) {
      if (soporte.obligatorio && !adjuntoDe(adjuntos, soporte.docCode)) errores[`documento.${soporte.docCode}`] = MENSAJE_FALTA_ARCHIVO;
    }
  }
  return errores;
}

/**
 * Lo que se escribe en `fieldValues` al guardar: solo la clave principal de cada declaración (las banderas `cambio_*` y similares
 * las deriva el servidor y lo que se mande en ellas se ignora). Una declaración activa con valor lo envía; sin valor se envía `null`
 * solo si es opcional (limpia lo anterior; una obligatoria vacía no se envía porque el back la rechazaría). Una que no se pide ahora
 * se limpia con `null` únicamente si había algo guardado (`guardados`).
 */
export function valoresDeDeclaraciones(
  d: DefinicionTipo,
  campos: Readonly<Record<string, string>>,
  transformaciones: readonly string[] = [],
  guardados: Readonly<Record<string, string>> = {},
): Record<string, string | null> {
  const valores: Record<string, string | null> = {};
  for (const decl of declaracionesDe(d)) {
    if (declaracionActiva(decl, transformaciones)) {
      const valor = (campos[decl.fieldKey] ?? "").trim();
      if (valor) valores[decl.fieldKey] = valor;
      else if (!decl.obligatoria) valores[decl.fieldKey] = null;
    } else if (!decl.obligatoria && (guardados[decl.fieldKey] ?? "").trim() !== "") {
      valores[decl.fieldKey] = null;
    }
  }
  return valores;
}

/** Llave de la pantalla de una declaración a partir de la clave de campo que nombra el back en un 422 (`valores.<fieldKey>`). */
export function claveDeCampoDeclaracion(d: DefinicionTipo, fieldKey: string): string | undefined {
  const decl = declaracionesDe(d).find((x) => x.fieldKey === fieldKey || x.fieldKeysExtra.includes(fieldKey));
  return decl ? `declaracion.${decl.clave}` : undefined;
}

// ── Secretaría de destino (traslado de cuenta) ──────────────────────────────────────────────────────────────────────

/** Clave de `fieldValues` donde queda el id de la secretaría de destino cuando el back no la nombra en una declaración. */
export const CLAVE_DESTINO_POR_DEFECTO = "transit_office_destino_id";
/** El servidor completa el nombre al crear el trámite; si ya viene en el borrador se usa para mostrar la elección. */
export const CLAVE_NOMBRE_DESTINO = "transit_office_destino_name";

export interface DestinoDelTipo {
  /** Clave de la declaración, si el back la trae (sirve para nombrar el error). */
  clave: string;
  fieldKey: string;
  obligatoria: boolean;
}

/** El trámite lleva secretaría de destino si el flag lo dice o el back trae su declaración. */
export function destinoDelTipo(d: DefinicionTipo): DestinoDelTipo | null {
  const decl = declaracionDestino(d);
  if (!decl && !d.flags.requiereDestino) return null;
  return { clave: decl?.clave ?? "destino", fieldKey: decl?.fieldKey || CLAVE_DESTINO_POR_DEFECTO, obligatoria: decl?.obligatoria ?? true };
}

/** Falta elegir la secretaría de destino: la guarda el servidor al elegirla, así que se mira el borrador y no un valor en pantalla. */
export function faltaDestino(d: DefinicionTipo, fieldValues: Readonly<Record<string, string>>): boolean {
  const destino = destinoDelTipo(d);
  return destino !== null && destino.obligatoria && (fieldValues[destino.fieldKey] ?? "").trim() === "";
}
