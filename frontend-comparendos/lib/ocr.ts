// Lógica pura de la carga por OCR (HU #13471 sobre la API de las HUs #13469/#13470; especificación GDC §6.18, §7.6).
// La API vuelve a validar todo; aquí solo se adelanta el rechazo y se arman etiquetas y cuerpos.
import type { Etiqueta } from "@/lib/etiquetas";
import { bogotaClock, type XlsxSheet } from "@/lib/xlsx";

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
  Pendiente: { texto: "Pendiente", clase: "bg-[var(--badge-neutral-bg)] text-[var(--badge-neutral-fg)]" },
  EnProcesamiento: { texto: "En procesamiento", clase: "bg-[var(--badge-info-bg)] text-[var(--badge-info-fg)]" },
  Completo: { texto: "Completo", clase: "bg-[var(--badge-success-bg)] text-[var(--badge-success-fg)]" },
  Incompleto: { texto: "Incompleto", clase: "bg-[var(--badge-warning-bg)] text-[var(--badge-warning-fg)]" },
  Fallido: { texto: "Fallido", clase: "bg-[var(--badge-danger-bg)] text-[var(--badge-danger-fg)]" },
  Duplicado: { texto: "Duplicado", clase: "bg-[var(--badge-purple-bg)] text-[var(--badge-purple-fg)]" },
  Registrado: { texto: "Registrado", clase: "bg-[var(--badge-success-bg)] text-[var(--badge-success-fg)] font-semibold" },
  Asociado: { texto: "Asociado", clase: "bg-[var(--badge-info-bg)] text-[var(--badge-info-fg)] font-semibold" },
  // §10: encolado en un lote; el trabajador de fondo lo asocia. Solo lectura mientras tanto.
  PorAsociar: { texto: "Asociando", clase: "bg-[var(--badge-info-bg)] text-[var(--badge-info-fg)]" },
  Descartado: { texto: "Descartado", clase: "bg-[var(--badge-neutral-bg)] text-[var(--badge-neutral-fg)] opacity-70" },
};

export const ESTADOS_DOCUMENTO = Object.keys(ESTADO_DOCUMENTO);

export const TIPO_FALLO: Record<string, string> = {
  Timeout: "La lectura tardó demasiado",
  Http5xx: "La lectura falló por un problema temporal",
  RateLimit: "La lectura está ocupada en este momento",
  Red: "La lectura falló por un problema de conexión",
  Ilegible: "Ilegible: no se pudieron leer los datos",
  ArchivoInvalido: "El archivo no se pudo leer",
};

/**
 * Motivo de un documento fallido (HU #13787, B12-7): primero el mensaje que guardó el servidor (por ejemplo «respuesta
 * cortada»), que es el real; la etiqueta del tipo solo cuando no hay mensaje.
 */
export function motivoFallo(tipo: string | null | undefined, mensaje: string | null | undefined): string {
  if (mensaje && mensaje.trim()) return mensaje.trim();
  return (tipo && TIPO_FALLO[tipo]) || "Falló";
}

export const FUENTE_DUPLICADO: Record<string, string> = {
  SIMIT: "SIMIT",
  MUNICIPAL: "Fuente municipal",
  OCR: "Otro documento escaneado",
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

export const TIPOS_DOCUMENTO = ["CC", "CE", "NIT", "TI", "PA", "CD", "RC", "CV"] as const;

/** Etiquetas amigables del selector de tipo de documento (OCR infractor). */
export const ETIQUETA_TIPO_DOCUMENTO: Record<(typeof TIPOS_DOCUMENTO)[number], string> = {
  CC: "CC",
  CE: "CE",
  NIT: "NIT (Persona jurídica)",
  TI: "TI",
  PA: "PA",
  CD: "CD",
  RC: "RC",
  CV: "CV",
};

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

/** El original se muestra como imagen salvo los PDF y los TIFF (los navegadores no pintan TIFF en <img>). */
export function vistaOriginal(tipoArchivo: string, nombre: string): "imagen" | "pdf" | "descarga" {
  if (tipoArchivo === "PDF") return "pdf";
  return [".tif", ".tiff"].includes(extension(nombre)) ? "descarga" : "imagen";
}

/** Acciones que admite un documento según su estado (la API manda; esto solo decide qué botones se muestran). */
export function accionesDocumento(d: { estado: string; reintentable: boolean; fuenteDuplicado: string | null; tipoFallo?: string | null }) {
  return {
    editar: ["Completo", "Incompleto", "Duplicado"].includes(d.estado) && d.fuenteDuplicado !== "ARCHIVO",
    registrar: d.estado === "Completo",
    descartar: ["Completo", "Incompleto", "Duplicado", "Fallido"].includes(d.estado),
    asociar: d.estado === "Duplicado" && d.fuenteDuplicado !== "ARCHIVO",
    reintentar: d.estado === "Fallido" && d.reintentable,
    // HU #13828: un incompleto o un ilegible se vuelve a leer; el tope de reprocesos lo dice la API (motivoNoReprocesable).
    reprocesar: d.estado === "Incompleto" || (d.estado === "Fallido" && d.tipoFallo === "Ilegible"),
  };
}

/** Estados finales del documento: solo lectura, sin validar ni pintar nada en rojo. */
export function esEstadoFinalDocumento(estado: string): boolean {
  return estado === "Asociado" || estado === "Registrado" || estado === "Descartado";
}

/** Documento que ya tiene el comparendo existente (contrato de asociar). */
export interface EvidenciaActual {
  archivoId: string;
  nombre: string;
  contentType: string;
  tamanoBytes: number;
  cargadaEn: string;
}

/** Datos del comparendo existente con el que choca un documento escaneado. */
export interface DuplicadoComparendo {
  tipo: "comparendo" | "documento";
  evidenciaActual?: EvidenciaActual | null;
  asociable?: boolean;
  motivoNoAsociable?: string | null;
  /** Qué pasará con el correo al conductor si se asocia ahora (lo decide el servidor con la misma regla del envío). */
  correoAlAsociar?: CorreoAlAsociar | null;
}

/** Lo que el servidor adelanta antes de asociar: si saldrá el correo con el documento y, si no, por qué. */
export interface CorreoAlAsociar {
  seEnviara: boolean;
  motivoNo: string | null;
}

/** Resultado del correo al asociar (respuesta del POST): enviado, en cola u omitido con su motivo. */
export interface CorreoAsociado {
  estado: "Enviado" | "Pendiente" | "Omitido";
  motivo: string | null;
}

/** El motivo del servidor se muestra tal cual; solo se cierra con punto si no lo trae. */
function motivoLiteral(motivo: string | null | undefined): string | null {
  const m = motivo?.trim();
  if (!m) return null;
  return /[.!?]$/.test(m) ? m : `${m}.`;
}

/**
 * Frase para el modal ANTES de confirmar. Sin dato del servidor no se promete nada. En reemplazo, el correo lleva el
 * documento nuevo.
 */
export function textoCorreoAlAsociar(c: CorreoAlAsociar | null | undefined, reemplazo: boolean): string | null {
  if (!c) return null;
  if (c.seEnviara) return reemplazo ? "Se enviará ahora al conductor un correo con el documento nuevo." : "Se enviará ahora al conductor un correo con este documento.";
  const motivo = motivoLiteral(c.motivoNo);
  return motivo ? `No se enviará correo al conductor. Motivo: ${motivo}` : "No se enviará correo al conductor.";
}

/**
 * Avisos de la pantalla DESPUÉS de asociar. Correo enviado o en cola: un solo aviso combinado. Cualquier otro estado
 * (Omitido o uno que no conocemos) es un aviso aparte con el motivo literal: la asociación igual se hizo.
 */
export function avisosAlAsociar(numero: string, reemplazar: boolean, correo: CorreoAsociado | null | undefined): { mensaje: string; tono: "success" | "warning" }[] {
  const base = reemplazar ? `Documento reemplazado en el comparendo ${numero}` : `Documento asociado al comparendo ${numero}`;
  if (!correo) return [{ mensaje: `${base}.`, tono: "success" }];
  if (correo.estado === "Enviado") return [{ mensaje: `${base} y enviado al conductor.`, tono: "success" }];
  if (correo.estado === "Pendiente") return [{ mensaje: `${base}. El correo al conductor saldrá en breve.`, tono: "success" }];
  const motivo = motivoLiteral(correo.motivo);
  return [
    { mensaje: `${base}.`, tono: "success" },
    { mensaje: motivo ? `No se envió correo al conductor. Motivo: ${motivo}` : "No se envió correo al conductor.", tono: "warning" },
  ];
}

/**
 * Qué se ofrece cuando el número ya existe (la API vuelve a validar): asociar si el comparendo no tiene documento,
 * comparar si ya tiene uno, o solo explicar por qué no se puede. Un duplicado contra otro documento no se asocia.
 */
export function vistaDuplicado(d: { estado: string; duplicado: DuplicadoComparendo | null }): "asociar" | "comparar" | "no-asociable" | null {
  const dup = d.duplicado;
  if (d.estado !== "Duplicado" || !dup || dup.tipo !== "comparendo") return null;
  if (dup.asociable !== true) return "no-asociable";
  return dup.evidenciaActual ? "comparar" : "asociar";
}

/**
 * Número sin letras iniciales, espacios, guiones ni puntos. Solo decide el aviso de UX («los números no coinciden»):
 * la regla de verdad (qué se guarda y la llave de duplicados) vive en NumeroComparendo.SinPrefijo del backend. Aquí no
 * se exige un mínimo de 10 dígitos a propósito: un aviso de más es inocuo, uno de menos esconde una diferencia.
 */
export function numeroBase(n: string | null | undefined): string {
  return (n ?? "").replace(/^[A-Za-z\s-]+/, "").replace(/[\s.-]/g, "").toUpperCase();
}

/** true si difieren en algo más que la letra inicial o el formato; sin uno de los dos números no se avisa. */
export function numerosDifieren(documento: string | null | undefined, sistema: string | null | undefined): boolean {
  const a = numeroBase(documento);
  const b = numeroBase(sistema);
  return a !== "" && b !== "" && a !== b;
}

/** Cómo se previsualiza un archivo según su tipo MIME o, si falta, su extensión. */
export function vistaPorContentType(contentType: string, nombre: string): "imagen" | "pdf" | "descarga" {
  const ct = contentType.toLowerCase();
  if (ct.includes("pdf")) return "pdf";
  if (ct.includes("tif")) return "descarga";
  if (ct.startsWith("image/")) return "imagen";
  return vistaOriginal(extension(nombre) === ".pdf" ? "PDF" : "", nombre);
}

/** Tamaño legible: «1,2 MB». */
export function tamanoLegible(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toLocaleString("es-CO", { maximumFractionDigits: 0 })} KB`;
  return `${(bytes / 1024 / 1024).toLocaleString("es-CO", { maximumFractionDigits: 1 })} MB`;
}

/** Por debajo de esto el modelo dijo dudar: es la única lectura útil de su confianza, que casi siempre sale en 95 % (B12-5). */
export const CONFIANZA_DUDOSA = 0.8;

/**
 * Qué campos hay que mirar y por qué (HU #13830, hallazgo B12-5). Reglas comprobables en vez del porcentaje del modelo:
 * obligatorio que falta o no sirve, número con 19 o 21 dígitos (HU #13829), la fecha de notificación (de ella cuentan
 * los plazos) y los pocos campos en los que el modelo dijo dudar.
 */
export function marcasRevision(d: {
  campos: { nombreCampo: string; confianza: number | null; valorFinal: string | null; valorCorregido: string | null }[];
  camposFaltantes: string[];
  avisoNumero?: string | null;
  numeroPorConfirmar?: boolean;
}): Record<string, string> {
  const marcas: Record<string, string> = {};
  for (const c of d.campos) {
    // El aviso del número vale también si lo escribió el operador: se calcula sobre el valor que quedó.
    if (d.camposFaltantes.includes(c.nombreCampo)) marcas[c.nombreCampo] = "Falta o no es válido.";
    // GDC punto 14: una vez corregido o confirmado por el operador, el número deja de marcarse.
    else if (c.nombreCampo === "numero_comparendo" && d.avisoNumero && d.numeroPorConfirmar !== false) marcas[c.nombreCampo] = d.avisoNumero;
    else if (c.valorCorregido !== null) continue;
    else if (c.nombreCampo === "fecha_notificacion" && c.valorFinal) marcas[c.nombreCampo] = "Confírmala: de ella cuentan los plazos.";
    else if (c.valorFinal && c.confianza !== null && c.confianza < CONFIANZA_DUDOSA) marcas[c.nombreCampo] = "La lectura de este campo es dudosa.";
  }
  return marcas;
}

/** Un documento tal como lo lista el detalle del paquete. */
export interface DocumentoPaquete {
  nombreArchivo: string;
  estado: string;
  numeroComparendoExtraido: string | null;
  camposFaltantes: string[];
  tipoFallo: string | null;
  mensajeError: string | null;
  fuenteDuplicado: string | null;
  motivoDescarte?: string | null;
  comparendoId: string | null;
  avisoNumero?: string | null;
  /** GDC punto 14: el número tiene aviso y nadie lo ha corregido ni confirmado; no se registra. */
  numeroPorConfirmar?: boolean;
  reprocesos?: number;
  /** GDC-EVID §9: por qué el documento no entra en «Asociar los N» y se revisa uno por uno. */
  revision?: "reemplazo" | "numeroDistinto" | null;
}

/**
 * GDC punto 14: «Registrar los N completos» no cuenta los completos con el número por confirmar (el servidor los omite).
 * Devuelve cuántos sí se registran y, si hay retenidos, qué tiene que hacer el operador.
 */
export function registroEnLote(completos: number, porConfirmar: number): { registrables: number; aviso: string | null } {
  const retenidos = Math.min(Math.max(porConfirmar, 0), completos);
  const aviso =
    retenidos === 0
      ? null
      : `${retenidos === 1 ? "1 documento completo tiene" : `${retenidos} documentos completos tienen`} el número por verificar y no se ${retenidos === 1 ? "registra" : "registran"} en lote. Ábrelo desde la tabla («Verificar número»), compara el número con el documento y corrígelo o confirma que está bien.`;
  return { registrables: completos - retenidos, aviso };
}

/** Fallo, duplicado, descarte o faltantes: lo que explica el estado de un documento, en una frase. */
export function motivoDocumento(d: DocumentoPaquete): string {
  if (d.estado === "Fallido") return motivoFallo(d.tipoFallo, d.mensajeError);
  if (d.estado === "Duplicado") return (d.fuenteDuplicado && FUENTE_DUPLICADO[d.fuenteDuplicado]) ?? "Ya existe";
  if (d.estado === "Asociado") return "Documento agregado a un comparendo existente";
  if (d.estado === "Descartado") return d.motivoDescarte ?? "Descartado";
  return d.camposFaltantes.map((c) => ETIQUETA_CAMPO[c] ?? c).join(", ");
}

/** Resumen del paquete en Excel (HU #13831): un renglón por documento y los totales en la nota. */
export function hojaPaquete(p: { nombre: string; creadoEn: string }, docs: DocumentoPaquete[]): XlsxSheet {
  const totales = ESTADOS_DOCUMENTO.map((e) => [ESTADO_DOCUMENTO[e].texto, docs.filter((d) => d.estado === e).length] as const)
    .filter(([, n]) => n > 0)
    .map(([t, n]) => `${t}: ${n}`);
  return {
    name: "Paquete OCR",
    columns: [
      { header: "Archivo", width: 34 },
      { header: "Estado", width: 16 },
      { header: "Número leído", width: 26 },
      { header: "Verificar número", width: 40 },
      { header: "Campos faltantes / motivo", width: 60 },
      { header: "Reprocesos", width: 12 },
      { header: "Registrado", width: 12 },
    ],
    rows: docs.map((d) => [
      d.nombreArchivo,
      ESTADO_DOCUMENTO[d.estado]?.texto ?? d.estado,
      d.numeroComparendoExtraido,
      d.avisoNumero ? (d.numeroPorConfirmar === false ? "Revisado por el operador" : d.avisoNumero) : null,
      motivoDocumento(d) || null,
      d.reprocesos ?? 0,
      d.comparendoId && d.estado !== "Asociado" ? "Sí" : "No",
    ]),
    notes: [
      `Paquete «${p.nombre}», creado ${fechaHora(p.creadoEn)} (hora de Colombia).`,
      `${docs.length} documentos. ${totales.join(", ")}.`,
    ],
  };
}

function fechaHora(iso: string): string {
  const d = bogotaClock(iso);
  if (!d) return iso;
  const dos = (n: number | undefined) => String(n ?? 0).padStart(2, "0");
  return `${dos(d.day)}/${dos(d.month)}/${d.year} ${dos(d.hour)}:${dos(d.minute)}`;
}

/** Última evaluación de una versión del prompt contra la publicada (solo SuperAdmin). */
export interface EvaluacionPrompt {
  id: string;
  estado: "EnCurso" | "Terminada" | "Fallida";
  documentos: number;
  campos: number;
  porcentajeCandidata: number;
  porcentajeVigente: number;
  vigenteNumero: number | null;
  aprobada: boolean;
  mensaje: string | null;
  creadoEn: string;
  terminadaEn: string | null;
  /** Aprobada, de la versión tal como está y contra la publicada de hoy: con ella se puede publicar. */
  habilita: boolean;
}

const porcentaje = (n: number) => `${n.toLocaleString("es-CO", { minimumFractionDigits: 1, maximumFractionDigits: 1 })} %`;

/** Chip de la evaluación: en curso, aprobada (si sigue sirviendo), no aprobada, desactualizada o fallida. */
export function etiquetaEvaluacion(e: EvaluacionPrompt | null): Etiqueta {
  if (!e) return { texto: "Sin evaluar", tono: "neutral" };
  if (e.estado === "EnCurso") return { texto: "Evaluando…", tono: "info" };
  if (e.estado === "Fallida") return { texto: "No se completó", tono: "warning" };
  if (!e.aprobada) return { texto: "No aprobada", tono: "danger" };
  return e.habilita ? { texto: "Aprobada", tono: "success" } : { texto: "Desactualizada", tono: "warning" };
}

/** «92,2 % vs. 91,1 % de la v1». */
export function resumenEvaluacion(e: EvaluacionPrompt): string | null {
  if (e.estado !== "Terminada") return null;
  return e.vigenteNumero === null ? porcentaje(e.porcentajeCandidata) : `${porcentaje(e.porcentajeCandidata)} vs. ${porcentaje(e.porcentajeVigente)} de la v${e.vigenteNumero}`;
}

/** La versión a la que se vuelve: la última que dejó de estar publicada. */
export function versionAnterior<T extends { publicada: boolean; retiradaEn: string | null }>(versiones: T[]): T | null {
  return versiones.filter((v) => !v.publicada && v.retiradaEn).reduce<T | null>((mejor, v) => (!mejor || v.retiradaEn! > mejor.retiradaEn! ? v : mejor), null);
}

// ---------------------------------------------------------------------------------------------------------------------
// GDC-EVID §9: asociar en lote («Asociar los N») y cola de revisión de reemplazos.
// ---------------------------------------------------------------------------------------------------------------------

/** Vista previa del lote (`GET /ocr/paquetes/{id}/asociacion-lote`). La preselección la hace el servidor. */
export interface VistaPreviaLote {
  asociables: number;
  correos: { saldran: number; conductores: number; omitidos: { motivo: string; cantidad: number }[] };
  porRevisar: { reemplazos: number; numeroDistinto: number };
  /** §10: el último lote del paquete (null si nunca hubo). */
  enCurso?: LoteEnCurso | null;
}

/** Último lote del paquete: avance mientras `porAsociar > 0` y, al terminar, el resumen. */
export interface LoteEnCurso {
  loteId: string;
  porAsociar: number;
  total: number;
  asociados: number;
  noAsociados: { documentoId: string; nombreArchivo: string; motivo: string | null }[];
  correosEnCamino: number;
  sinCorreo: { motivo: string | null; cantidad: number }[];
  solicitadoPor: string | null;
  solicitadoEn: string;
  terminadoEn: string | null;
  /** Último documento procesado (o solicitadoEn si aún ninguno). Aditivo: puede no venir. */
  ultimoAvanceEn?: string | null;
}

const plural = (n: number, uno: string, varios: string) => (n === 1 ? uno : varios.replace("{n}", String(n)));

/** Etiqueta del botón primario del paquete. */
export const etiquetaAsociarLote = (n: number) => plural(n, "Asociar el documento", "Asociar los {n}");

/** Textos del modal de confirmación, a partir de la vista previa del servidor. */
export function textosVistaPreviaLote(v: VistaPreviaLote): { documentos: string; correos: string; porRevisar: string[] } {
  const { saldran, conductores } = v.correos;
  const correos =
    saldran === 0
      ? "No saldrá ningún correo al conductor."
      : `${plural(saldran, "Saldrá 1 correo", "Saldrán {n} correos")} a ${plural(conductores, "1 conductor", "{n} conductores")} (cada conductor recibe uno por comparendo).`;
  const porRevisar: string[] = [];
  if (v.porRevisar.reemplazos > 0)
    porRevisar.push(plural(v.porRevisar.reemplazos, "Queda 1 reemplazo para revisar uno por uno.", "Quedan {n} reemplazos para revisar uno por uno."));
  if (v.porRevisar.numeroDistinto > 0)
    porRevisar.push(
      plural(v.porRevisar.numeroDistinto, "1 documento tiene el número distinto al del sistema: se revisa uno por uno.", "{n} documentos tienen el número distinto al del sistema: se revisan uno por uno."),
    );
  return { documentos: plural(v.asociables, "Se asociará 1 documento a su comparendo.", "Se asociarán {n} documentos a sus comparendos."), correos, porRevisar };
}

/** Respuesta de `POST /ocr/paquetes/{id}/asociar-lote` (§10): el servidor encola y un trabajador asocia en segundo plano. */
export interface LoteEncolado {
  encolados: number;
  /** null cuando no encoló nada (doble clic o nada elegible): no se crea lote. */
  loteId: string | null;
}

/** Aviso tras confirmar: el lote ya no se espera frente a un modal. */
export const avisoLoteEncolado = (n: number) => `${plural(n, "Asociando 1 documento", "Asociando {n} documentos")} en segundo plano; puedes seguir trabajando.`;

/** Si el POST no encoló nada y tampoco hay un lote del paquete que mostrar en la franja. */
export const AVISO_SIN_DOCUMENTOS_LOTE = "No había documentos por asociar.";

/** Texto de la franja mientras el lote avanza (procesados = los que ya no están por asociar). */
export function textoAvanceLote(l: LoteEnCurso): string {
  const hechos = Math.max(0, l.total - l.porAsociar);
  return `Asociando ${hechos} de ${l.total}…`;
}

/** Resumen del último lote, cuando terminó. */
export function textosResumenLote(l: LoteEnCurso): { titulo: string; correos: string | null } {
  const titulo = plural(l.asociados, "Se asoció 1 documento a su comparendo.", "Se asociaron {n} documentos a sus comparendos.");
  const correos =
    l.correosEnCamino === 0
      ? null
      : plural(l.correosEnCamino, "1 correo con el documento va en camino al conductor.", "{n} correos con el documento van en camino a los conductores.");
  return { titulo, correos };
}

/** El servidor garantiza motivo; si algún día llega nulo o vacío, la franja no muestra «null» ni un hueco. */
export const motivoLote = (m: string | null | undefined) => m?.trim() || "Sin motivo registrado";

/** Minutos sin avance a partir de los cuales la franja lo avisa. */
export const MINUTOS_SIN_AVANCE_AVISO = 5;

/**
 * Si el lote sigue en curso y lleva más de 5 minutos sin procesar un documento, el aviso para la franja (el servidor lo
 * retoma solo). Sin `ultimoAvanceEn`, terminado o con fecha inválida: null.
 */
export function avisoSinAvanceLote(l: LoteEnCurso, ahora: number = Date.now()): string | null {
  if (l.porAsociar <= 0 || !l.ultimoAvanceEn) return null;
  const desde = Date.parse(l.ultimoAvanceEn);
  if (Number.isNaN(desde)) return null;
  const minutos = Math.floor((ahora - desde) / 60000);
  if (minutos <= MINUTOS_SIN_AVANCE_AVISO) return null;
  return `Sin avance desde hace ${minutos} minutos; se retomará automáticamente.`;
}

const CLAVE_RESUMEN_CERRADO = "comparendos.ocr.lote-resumen-cerrado:";

/** ¿La gestora ya cerró el resumen de este lote? (por navegador; si el almacenamiento falla, se muestra). */
export function resumenLoteCerrado(loteId: string): boolean {
  try {
    return window.localStorage.getItem(CLAVE_RESUMEN_CERRADO + loteId) === "1";
  } catch {
    return false;
  }
}

export function cerrarResumenLote(loteId: string): void {
  try {
    window.localStorage.setItem(CLAVE_RESUMEN_CERRADO + loteId, "1");
  } catch {
    // Sin almacenamiento (privado, bloqueado): el resumen se cierra en esta vista y vuelve a salir al recargar.
  }
}

/** Documentos del paquete que esperan revisión de reemplazo, en el orden del listado. */
export function colaReemplazos(docs: { id: string; estado: string; revision?: DocumentoPaquete["revision"] }[]): string[] {
  return docs.filter((d) => d.revision === "reemplazo" && d.estado === "Duplicado").map((d) => d.id);
}

/** Dónde va la revisión de reemplazos: viaja en la URL del documento (`?cola=reemplazos&paquete=…`). */
export interface PasoCola {
  paquete: string;
  hechos: number;
  total: number;
  reemplazados: number;
  descartados: number;
}

const entero = (v: string | null) => {
  const n = Number(v);
  return Number.isInteger(n) && n >= 0 ? n : 0;
};

export function leerCola(q: URLSearchParams): PasoCola | null {
  const paquete = q.get("paquete");
  if (q.get("cola") !== "reemplazos" || !paquete) return null;
  const hechos = entero(q.get("hechos"));
  return { paquete, hechos, total: Math.max(entero(q.get("total")), hechos + 1), reemplazados: entero(q.get("reemplazados")), descartados: entero(q.get("descartados")) };
}

export function urlCola(documentoId: string, c: PasoCola): string {
  const q = new URLSearchParams({ cola: "reemplazos", paquete: c.paquete, hechos: String(c.hechos), total: String(c.total), reemplazados: String(c.reemplazados), descartados: String(c.descartados) });
  return `/ocr/documentos/${documentoId}?${q.toString()}`;
}

/**
 * Tras reemplazar, descartar o saltar el actual (saltar: ya no estaba por revisar, p. ej. otra persona lo resolvió): el siguiente pendiente (el total se recalcula con lo que queda, por si otra
 * persona avanzó la cola) o, si no queda ninguno, el paquete con el resumen.
 */
export function siguienteEnCola(pendientes: string[], actual: string, c: PasoCola, decision: "reemplazado" | "descartado" | "saltado"): string {
  const hechos = c.hechos + 1;
  const reemplazados = c.reemplazados + (decision === "reemplazado" ? 1 : 0);
  const descartados = c.descartados + (decision === "descartado" ? 1 : 0);
  const resto = pendientes.filter((p) => p !== actual);
  if (resto.length > 0) return urlCola(resto[0], { paquete: c.paquete, hechos, total: hechos + resto.length, reemplazados, descartados });
  const q = new URLSearchParams({ cola: "terminada", reemplazados: String(reemplazados), descartados: String(descartados) });
  return `/ocr/paquetes/${c.paquete}?${q.toString()}`;
}

/** Resumen al volver al paquete. */
export function resumenCola(q: URLSearchParams): string | null {
  if (q.get("cola") !== "terminada") return null;
  const r = entero(q.get("reemplazados"));
  const d = entero(q.get("descartados"));
  const partes = [r ? plural(r, "1 reemplazado", "{n} reemplazados") : null, d ? plural(d, "1 descartado", "{n} descartados") : null].filter(Boolean);
  return `Terminaste la revisión de reemplazos${partes.length ? `: ${partes.join(" y ")}` : ""}.`;
}
