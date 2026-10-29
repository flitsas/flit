// Paso Requisitos (HU F4 y K6). Módulo puro, sin React ni red: qué tarjetas aplican al tipo, cómo se valida cada campo y cuándo está
// completo el paso. Todo sale de la definición del tipo (flags, campos y documentos): ningún tipo de trámite está nombrado aquí.
import type { Adjunto, CampoDefinicion, DefinicionTipo, DocumentoRequerido, PasoDefinicion } from "@/lib/api/cliente";
import { numeroMoneda } from "@/lib/formato";
import { GRUPO_POR_SECCION } from "@/lib/tramite/pasos";

// ── Archivos ────────────────────────────────────────────────────────────────────────────────────────────────────────

export const MAX_BYTES_ARCHIVO = 20 * 1024 * 1024;
export const EXTENSIONES_PERMITIDAS = [".pdf", ".jpg", ".jpeg", ".png", ".webp"] as const;
export const ACCEPT_ARCHIVOS = EXTENSIONES_PERMITIDAS.join(",");
export const AYUDA_ARCHIVOS = "PDF, JPG, PNG, WEBP · hasta 20.0 MB";
export const MENSAJE_FALTA_ARCHIVO = "Adjunta el archivo requerido";

/** Motivo por el que un archivo no se sube (se revisa en el navegador antes de enviarlo), o `null` si es válido. */
export function errorDeArchivo(archivo: Pick<File, "name" | "size">): string | null {
  const nombre = archivo.name.toLowerCase();
  if (!EXTENSIONES_PERMITIDAS.some((e) => nombre.endsWith(e))) return "Solo se aceptan archivos PDF, JPG, PNG o WEBP.";
  if (archivo.size === 0) return "El archivo está vacío.";
  if (archivo.size > MAX_BYTES_ARCHIVO) return "El archivo supera los 20 MB permitidos.";
  return null;
}

/** Tamaño legible, en megabytes con un decimal: «2.5 MB». */
export const tamanoLegible = (bytes: number): string => `${(bytes / (1024 * 1024)).toFixed(1)} MB`;

/** El archivo vigente de un documento: el más reciente que se subió con su `docCode`. */
export function adjuntoDe(adjuntos: readonly Adjunto[], docCode: string): Adjunto | undefined {
  return adjuntos.filter((a) => a.tipo === docCode).sort((a, b) => b.creadoEn.localeCompare(a.creadoEn))[0];
}

/** Lo que el ciudadano ya eligió y con lo que se decide si un documento condicional se muestra. */
export interface ContextoCondicion {
  tienePrenda: boolean;
  transformaciones: readonly string[];
  campos: Readonly<Record<string, string>>;
  hayPersonaJuridica: boolean;
}

export const contextoVacio: ContextoCondicion = { tienePrenda: false, transformaciones: [], campos: {}, hayPersonaJuridica: false };

/**
 * Un documento sin condición siempre se muestra. Los textos que publica FLIT 2 se resuelven con lo que el ciudadano
 * ya llenó. Un texto que el portal no reconoce se sigue mostrando (el servidor decide al radicar) y no se exige aquí.
 */
export function documentoVisible(doc: DocumentoRequerido, ctx: ContextoCondicion = contextoVacio): boolean {
  const c = doc.condicion.trim().toLowerCase();
  if (c === "") return true;
  if (c.includes("persona natural")) return false;
  if (c.includes("persona jurídica") || c.includes("persona juridica") || c.includes("nit")) return !ctx.hayPersonaJuridica;
  if (c.includes("tramitador") || c.includes("mandato")) return false;
  if (c.includes("causal de cancelación") || c.includes("causal de cancelacion")) return false;
  if (c.includes("leasing")) return menciona(ctx, "leasing");
  if (c.includes("prenda")) return ctx.tienePrenda;
  if (c.includes("servicio especial")) return menciona(ctx, "especial");
  if (c.includes("carrocer")) return ctx.transformaciones.some((t) => t.toUpperCase() === "CAMBIO_CARROCERIA");
  return true;
}

/** La condición es una de las que el portal sabe resolver (si no, no se exige en el navegador). */
function condicionConocida(doc: DocumentoRequerido): boolean {
  const c = doc.condicion.trim().toLowerCase();
  if (c === "") return false;
  return (
    c.includes("persona natural") ||
    c.includes("persona jurídica") ||
    c.includes("persona juridica") ||
    c.includes("nit") ||
    c.includes("tramitador") ||
    c.includes("mandato") ||
    c.includes("causal de cancelación") ||
    c.includes("causal de cancelacion") ||
    c.includes("leasing") ||
    c.includes("prenda") ||
    c.includes("servicio especial") ||
    c.includes("carrocer")
  );
}

function menciona(ctx: ContextoCondicion, texto: string): boolean {
  const needle = texto.toLowerCase();
  return Object.entries(ctx.campos).some(([k, v]) => k.toLowerCase().includes(needle) || v.toLowerCase().includes(needle));
}

/** Obligatorio y, si tiene una condición que el portal entiende, solo cuando esa condición se cumple. */
export function esExigible(doc: DocumentoRequerido, ctx: ContextoCondicion = contextoVacio): boolean {
  if (!doc.obligatorio || !documentoVisible(doc, ctx)) return false;
  return doc.condicion.trim() === "" || condicionConocida(doc);
}

/** El cobro está fuera de esta entrega del portal: el comprobante de pago no se ofrece en Requisitos aunque la definición lo liste. */
export const DOC_COMPROBANTE_PAGO = "comprobante_pago";

/** Los documentos de este paso, por su orden en la definición. */
export const documentosOrdenados = (definicion: DefinicionTipo): DocumentoRequerido[] =>
  [...definicion.documentos].filter((d) => d.docCode !== DOC_COMPROBANTE_PAGO).sort((a, b) => a.orden - b.orden);

// ── Tarjetas que aplican al tipo ────────────────────────────────────────────────────────────────────────────────────

export interface TarjetasRequisitos {
  comercial: boolean;
  prenda: boolean;
  /** La prenda es obligatoria por definición (no se pregunta, se piden acreedor y NIT). */
  prendaObligatoria: boolean;
  documentos: boolean;
  /** Campos propios del tipo: un grupo por paso de la definición que los trae. */
  campos: PasoDefinicion[];
  observaciones: boolean;
  transformaciones: boolean;
}

const tieneSeccion = (d: DefinicionTipo, tipo: string): boolean => d.pasos.some((p) => p.sectionTypes.includes(tipo));

/** Las secciones de la definición que pertenecen al paso Requisitos, en el orden de la definición. */
export const seccionesDeRequisitos = (d: DefinicionTipo): string[] => d.pasos.flatMap((p) => p.sectionTypes).filter((s) => GRUPO_POR_SECCION[s] === "requisitos");

/**
 * Tarjetas del paso para este tipo. Una tarjeta aparece solo si la definición tiene la sección que la pinta y su capacidad lo
 * permite (los flags); las demás no se muestran ni dejan huecos. Las observaciones acompañan a cualquier paso de requisitos.
 */
export function tarjetasDeRequisitos(d: DefinicionTipo): TarjetasRequisitos {
  const admitePrenda = d.flags.requierePrenda || d.flags.permitePrendaComplementaria;
  return {
    comercial: tieneSeccion(d, "commercial") && d.flags.requiereComercial,
    prenda: tieneSeccion(d, "prenda_decision") && admitePrenda,
    prendaObligatoria: d.flags.requierePrenda,
    documentos: tieneSeccion(d, "document_checklist") && documentosOrdenados(d).length > 0,
    campos: d.pasos.filter((p) => p.campos.length > 0 && GRUPO_POR_SECCION[p.sectionTypes[0] ?? ""] === "requisitos"),
    observaciones: seccionesDeRequisitos(d).length > 0,
    transformaciones: seccionesDeRequisitos(d).length > 0 && d.flags.permiteTransformaciones,
  };
}

// ── Transformaciones ────────────────────────────────────────────────────────────────────────────────────────────────

/** Las que el portal cotiza (mismos códigos que valida el back). */
export const TRANSFORMACIONES: readonly { codigo: string; etiqueta: string }[] = [
  { codigo: "CAMBIO_COLOR", etiqueta: "Cambio de color" },
  { codigo: "CONVERSION_COMBUSTIBLE", etiqueta: "Conversión de combustible" },
  { codigo: "CAMBIO_CARROCERIA", etiqueta: "Cambio de carrocería" },
];

// ── Validación de campos ────────────────────────────────────────────────────────────────────────────────────────────

const MAX_TEXTO = 500;
const MAX_TEXTO_LARGO = 2000;
export const MENSAJE_OBLIGATORIO = "Este campo es obligatorio";

/** Error de un campo del tipo (misma regla que el back: tipo, opciones y obligatoriedad), o `undefined` si es válido. */
export function errorDeCampo(campo: CampoDefinicion, valor: string): string | undefined {
  const v = valor.trim();
  if (v === "") return campo.obligatorio ? MENSAJE_OBLIGATORIO : undefined;
  switch (campo.tipo.toLowerCase()) {
    case "number":
      return /^-?\d+(\.\d+)?$/.test(v) ? undefined : "Escribe un número";
    case "date":
      return /^\d{4}-\d{2}-\d{2}$/.test(v) && !Number.isNaN(new Date(`${v}T00:00:00Z`).getTime()) ? undefined : "Elige una fecha válida";
    case "checkbox":
      return v === "true" || v === "false" ? undefined : "Elige una opción";
    case "select":
      return campo.opciones.length === 0 || campo.opciones.includes(v) ? undefined : "Elige una de las opciones";
    case "textarea":
      return v.length <= MAX_TEXTO_LARGO ? undefined : `Usa hasta ${MAX_TEXTO_LARGO} caracteres`;
    default:
      return v.length <= MAX_TEXTO ? undefined : `Usa hasta ${MAX_TEXTO} caracteres`;
  }
}

export const valorComercialInvalido = (v: string): string | undefined => {
  const n = numeroMoneda(v);
  return Number.isFinite(n) && n > 0 ? undefined : "Ingresa el valor de venta";
};

export const nitAcreedorInvalido = (v: string): string | undefined => (/^\d{8,10}$/.test(v) ? undefined : "Ingresa el NIT del acreedor (8 a 10 dígitos)");

export const acreedorInvalido = (v: string): string | undefined => (v.trim() === "" ? "Campo obligatorio" : v.trim().length > 150 ? "El nombre del acreedor es demasiado largo" : undefined);

// ── Qué falta ───────────────────────────────────────────────────────────────────────────────────────────────────────

export interface EntradaRequisitos {
  campos: Readonly<Record<string, string>>;
  valorVenta: string;
  tienePrenda: boolean;
  acreedor: string;
  nitAcreedor: string;
  observaciones: string;
  transformaciones?: readonly string[];
  hayPersonaJuridica?: boolean;
}

/** Errores de lo escrito, por llave estable (la misma con la que el back nombra el campo): `campo.<key>`, `valorComercial`, `prenda.acreedor`… */
export function erroresDeRequisitos(d: DefinicionTipo, adjuntos: readonly Adjunto[], e: EntradaRequisitos): Record<string, string> {
  const t = tarjetasDeRequisitos(d);
  const errores: Record<string, string> = {};
  for (const paso of t.campos) {
    for (const campo of paso.campos) {
      const msg = errorDeCampo(campo, e.campos[campo.fieldKey] ?? "");
      if (msg) errores[`campo.${campo.fieldKey}`] = msg;
    }
  }
  if (t.comercial) {
    const msg = valorComercialInvalido(e.valorVenta);
    if (msg) errores["valorComercial"] = msg;
  }
  if (t.prenda && (t.prendaObligatoria || e.tienePrenda)) {
    const a = acreedorInvalido(e.acreedor);
    const n = nitAcreedorInvalido(e.nitAcreedor);
    if (a) errores["prenda.acreedor"] = a;
    if (n) errores["prenda.nitAcreedor"] = n;
  }
  if (t.documentos) {
    const ctx: ContextoCondicion = {
      tienePrenda: e.tienePrenda,
      transformaciones: e.transformaciones ?? [],
      campos: e.campos,
      hayPersonaJuridica: e.hayPersonaJuridica ?? false,
    };
    for (const doc of documentosOrdenados(d).filter((doc) => esExigible(doc, ctx))) if (!adjuntoDe(adjuntos, doc.docCode)) errores[`documento.${doc.docCode}`] = MENSAJE_FALTA_ARCHIVO;
  }
  if (t.observaciones && e.observaciones.trim().length > MAX_TEXTO_LARGO) errores["observaciones"] = `Usa hasta ${MAX_TEXTO_LARGO} caracteres`;
  return errores;
}

export const MOTIVO_REQUISITOS = "Completa los datos y documentos obligatorios para continuar.";
