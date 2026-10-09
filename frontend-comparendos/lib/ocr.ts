// Lógica pura de la carga por OCR (HU #13471 sobre la API de las HUs #13469/#13470; especificación GDC §6.18, §7.6).
// La API vuelve a validar todo; aquí solo se adelanta el rechazo y se arman etiquetas y cuerpos.
import type { Etiqueta } from "@/lib/etiquetas";

export const MAXIMO_DOCUMENTOS = 200;
export const MAXIMO_BYTES = 20 * 1024 * 1024;
const EXTENSIONES = [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".pdf"];

/** Un archivo elegido para el paquete: válido o con el motivo del rechazo inmediato. */
export interface ArchivoElegido {
  archivo: File;
  error: string | null;
}

function extension(nombre: string): string {
  const i = nombre.lastIndexOf(".");
  return i < 0 ? "" : nombre.slice(i).toLowerCase();
}

/** Formato JPG, PNG, TIFF o PDF y tamaño entre 1 byte y 20 MB (RN-051). null = válido. */
export function errorArchivoOcr(archivo: Pick<File, "name" | "size">): string | null {
  if (!EXTENSIONES.includes(extension(archivo.name))) return "Formato no permitido: solo JPG, PNG, TIFF o PDF.";
  if (archivo.size === 0) return "El archivo está vacío.";
  if (archivo.size > MAXIMO_BYTES) return "Supera 20 MB.";
  return null;
}

/**
 * Agrega archivos a la selección: descarta repetidos (mismo nombre y tamaño) y marca los inválidos. Los que pasen del
 * tope de 200 válidos quedan rechazados con ese motivo.
 */
export function agregarArchivos(actuales: ArchivoElegido[], nuevos: File[]): ArchivoElegido[] {
  const clave = (f: Pick<File, "name" | "size">) => `${f.name}|${f.size}`;
  const vistos = new Set(actuales.map((a) => clave(a.archivo)));
  let validos = actuales.filter((a) => !a.error).length;
  const resultado = [...actuales];
  for (const archivo of nuevos) {
    if (vistos.has(clave(archivo))) continue;
    vistos.add(clave(archivo));
    let error = errorArchivoOcr(archivo);
    if (!error && validos >= MAXIMO_DOCUMENTOS) error = `Un paquete lleva máximo ${MAXIMO_DOCUMENTOS} archivos.`;
    if (!error) validos++;
    resultado.push({ archivo, error });
  }
  return resultado;
}

/** Lo que falta antes de crear el paquete; vacío = listo. */
export function erroresPaquete(nombre: string, elegidos: ArchivoElegido[]): string[] {
  const errores: string[] = [];
  const n = nombre.trim();
  if (!n) errores.push("Escribe el nombre del paquete.");
  else if (n.length > 150) errores.push("El nombre admite hasta 150 caracteres.");
  const validos = elegidos.filter((a) => !a.error).length;
  if (validos === 0) errores.push("Agrega al menos un archivo válido.");
  if (validos > MAXIMO_DOCUMENTOS) errores.push(`Un paquete lleva máximo ${MAXIMO_DOCUMENTOS} archivos.`);
  return errores;
}

/** Cuerpo multipart de POST /ocr/paquetes: solo los archivos válidos; `iniciar` encola el procesamiento de una vez. */
export function armarPaquete(nombre: string, elegidos: ArchivoElegido[], iniciar: boolean): FormData {
  const form = new FormData();
  form.append("nombre", nombre.trim());
  if (iniciar) form.append("iniciar", "true");
  for (const a of elegidos) if (!a.error) form.append("archivos", a.archivo, a.archivo.name);
  return form;
}

export const ESTADO_PAQUETE: Record<string, Etiqueta> = {
  Pendiente: { texto: "Pendiente", tono: "neutral" },
  EnProcesamiento: { texto: "En procesamiento", tono: "info" },
  Procesado: { texto: "Procesado", tono: "success" },
  ProcesadoConErrores: { texto: "Con errores", tono: "warning" },
  Cerrado: { texto: "Cerrado", tono: "neutral" },
};

export const ESTADOS_PAQUETE = Object.keys(ESTADO_PAQUETE);

/** Estados de documento con su color de §7.6 (el morado de DUPLICADO no existe en los tonos de @flit/ui). */
export const ESTADO_DOCUMENTO: Record<string, { texto: string; clase: string }> = {
  Pendiente: { texto: "Pendiente", clase: "bg-[#EEF1F6] text-[#475569] dark:bg-white/10 dark:text-slate-300" },
  EnProcesamiento: { texto: "En procesamiento", clase: "bg-[#E8EEFF] text-[#3B4FD6] dark:bg-[#557EFF]/20 dark:text-[#8FA8FF]" },
  Completo: { texto: "Completo", clase: "bg-[#E7F6EC] text-[#1E7B3C] dark:bg-emerald-500/15 dark:text-emerald-300" },
  Incompleto: { texto: "Incompleto", clase: "bg-[#FFF1E5] text-[#B45309] dark:bg-orange-500/15 dark:text-orange-300" },
  Fallido: { texto: "Fallido", clase: "bg-[#FDECEC] text-[#B42318] dark:bg-red-500/15 dark:text-red-300" },
  Duplicado: { texto: "Duplicado", clase: "bg-[#F1EAFE] text-[#6D28D9] dark:bg-violet-500/15 dark:text-violet-300" },
  Registrado: { texto: "Registrado", clase: "bg-[#E7F6EC] text-[#14532D] dark:bg-emerald-500/25 dark:text-emerald-200" },
  Descartado: { texto: "Descartado", clase: "bg-[#F4F5F7] text-[#94A3B8] dark:bg-white/5 dark:text-slate-500" },
};

export const ESTADOS_DOCUMENTO = Object.keys(ESTADO_DOCUMENTO);

export const TIPO_FALLO: Record<string, string> = {
  Timeout: "El proveedor no respondió a tiempo",
  Http5xx: "El proveedor falló (5xx)",
  RateLimit: "El proveedor limitó las peticiones (429)",
  Red: "Error de red",
  Ilegible: "Ilegible: el modelo no extrajo datos",
  ArchivoInvalido: "Archivo inválido",
};

export const FUENTE_DUPLICADO: Record<string, string> = {
  SIMIT: "SIMIT",
  MUNICIPAL: "Fuente municipal",
  OCR: "Otro documento OCR",
  ARCHIVO: "El mismo archivo ya se cargó",
};

/** Contadores de un paquete tal como los devuelve la API. */
export interface ContadoresPaquete {
  cantidadDocumentos: number;
  cantidadPendientes: number;
}

/** «45/50 procesados»: todo lo que ya salió de PENDIENTE o EN_PROCESAMIENTO. */
export function progreso(p: ContadoresPaquete): { procesados: number; total: number; porcentaje: number; texto: string } {
  const total = Math.max(0, p.cantidadDocumentos);
  const procesados = Math.min(total, Math.max(0, total - p.cantidadPendientes));
  const porcentaje = total === 0 ? 0 : Math.round((procesados / total) * 100);
  return { procesados, total, porcentaje, texto: `${procesados}/${total} procesados` };
}

/** Se vuelve a consultar mientras algún paquete o documento siga en cola. */
export function hayEnCurso(estados: string[]): boolean {
  return estados.some((e) => e === "EnProcesamiento" || e === "Pendiente");
}

/** Nombres legibles de los campos que pide el prompt (§8.5); los obligatorios son los de RN-053. */
export const ETIQUETA_CAMPO: Record<string, string> = {
  numero_comparendo: "Número de comparendo",
  tipo_documento_infractor: "Tipo de documento del infractor",
  numero_documento_infractor: "Número de documento del infractor",
  nombre_infractor: "Nombre del infractor",
  codigo_infraccion: "Código de infracción",
  fecha_infraccion: "Fecha de la infracción",
  placa: "Placa",
  secretaria: "Secretaría / organismo",
  valor: "Valor",
  descripcion_infraccion: "Descripción de la infracción",
  fecha_notificacion: "Fecha de notificación",
  hora_infraccion: "Hora de la infracción",
  codigo_organismo: "Código del organismo",
  es_fotomulta: "¿Es fotomulta?",
  direccion: "Dirección",
  tipo_vehiculo: "Tipo de vehículo",
};

export const TIPOS_DOCUMENTO = ["CC", "CE", "NIT", "TI", "PA", "CD", "RC", "CV"];

/** Cómo se edita cada campo en el formulario de revisión. */
export function tipoEntrada(campo: string): "fecha" | "hora" | "documento" | "booleano" | "texto" {
  if (campo === "fecha_infraccion" || campo === "fecha_notificacion") return "fecha";
  if (campo === "hora_infraccion") return "hora";
  if (campo === "tipo_documento_infractor") return "documento";
  if (campo === "es_fotomulta") return "booleano";
  return "texto";
}

/** Un valor de fecha del LLM («dd/mm/aaaa» o «aaaa-mm-dd») al formato de un input date; si no se entiende, vacío. */
export function aFechaInput(valor: string | null | undefined): string {
  const v = (valor ?? "").trim();
  if (/^\d{4}-\d{2}-\d{2}$/.test(v)) return v;
  const m = /^(\d{1,2})[/-](\d{1,2})[/-](\d{4})$/.exec(v);
  return m ? `${m[3]}-${m[2].padStart(2, "0")}-${m[1].padStart(2, "0")}` : "";
}

/** Valor inicial del formulario para un campo según cómo se edita. */
export function valorInicial(campo: string, valor: string | null): string {
  if (tipoEntrada(campo) === "fecha") return aFechaInput(valor) || (valor ?? "");
  if (tipoEntrada(campo) === "booleano") {
    const v = (valor ?? "").trim().toLowerCase();
    return ["true", "si", "sí", "1"].includes(v) ? "true" : ["false", "no", "0"].includes(v) ? "false" : "";
  }
  return valor ?? "";
}

/**
 * Lo que cambió el operador frente al valor que se mostró: solo esos campos van en el PUT (cada uno queda en el log de
 * ediciones, RN-057). Un vacío se envía como null (borra el valor).
 */
export function cambiosCampos(iniciales: Record<string, string>, editados: Record<string, string>): Record<string, string | null> {
  const cambios: Record<string, string | null> = {};
  for (const [campo, valor] of Object.entries(editados)) {
    if (valor.trim() === (iniciales[campo] ?? "").trim()) continue;
    cambios[campo] = valor.trim() === "" ? null : valor.trim();
  }
  return cambios;
}

/** Confianza 0–1 → «93 %» y un tono: baja (< 0,6), media (< 0,85) o alta. */
export function confianza(c: number | null | undefined): { texto: string; tono: "alta" | "media" | "baja" } | null {
  if (c === null || c === undefined) return null;
  const tono = c < 0.6 ? "baja" : c < 0.85 ? "media" : "alta";
  return { texto: `${Math.round(c * 100)} %`, tono };
}

/** El original se muestra como imagen salvo los PDF y los TIFF (los navegadores no pintan TIFF en <img>). */
export function vistaOriginal(tipoArchivo: string, nombre: string): "imagen" | "pdf" | "descarga" {
  if (tipoArchivo === "PDF") return "pdf";
  return [".tif", ".tiff"].includes(extension(nombre)) ? "descarga" : "imagen";
}

/** Acciones que admite un documento según su estado (la API manda; esto solo decide qué botones se muestran). */
export function accionesDocumento(d: { estado: string; reintentable: boolean; fuenteDuplicado: string | null }) {
  return {
    editar: ["Completo", "Incompleto", "Duplicado"].includes(d.estado) && d.fuenteDuplicado !== "ARCHIVO",
    registrar: d.estado === "Completo",
    descartar: ["Completo", "Incompleto", "Duplicado", "Fallido"].includes(d.estado),
    reintentar: d.estado === "Fallido" && d.reintentable,
  };
}
