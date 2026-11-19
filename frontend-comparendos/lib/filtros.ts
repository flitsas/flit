// Filtros de la bandeja (§7.1) y su traducción a la query de GET /comparendos. Lógica pura y probada.
import type { Query } from "@/lib/api";
import { CAMBIO_CONTRAVENTOR, ESTADO_LEGAL, ESTADO_PROCESO, FUENTE, ULTIMO_AVISO, type Etiqueta } from "@/lib/etiquetas";
import { formatoFecha } from "@/lib/format/date";

/** «Radar» no está en la barra: llega desde el KPI del dashboard (§7.8). */
export type FiltroRapido = "ProximosAVencer" | "SinContraventor" | "RechazadosOt" | "Radar";
export type FiltroJuridico = "CaducidadInmediata" | "NoImpugnables" | "C038";
export type TriEstado = "" | "true" | "false";

/** HU #13868: los filtros de lista que admiten «no es». */
export type CampoNegable = "nits" | "placas" | "numeros" | "organismos" | "estadoProceso" | "ultimoAviso" | "cambioContraventor" | "fuente";

export interface FiltrosBandeja {
  /** HU #13870: la caja de búsqueda; busca dentro del número o de la placa. */
  buscar: string;
  nits: string[];
  /** HU #13868: listas pegadas desde Excel; coinciden completas. */
  placas: string[];
  numeros: string[];
  organismos: string[];
  imposicionDesde: string;
  imposicionHasta: string;
  notificacionDesde: string;
  notificacionHasta: string;
  registroDesde: string;
  registroHasta: string;
  estadoLegal: string[];
  estadoProceso: string[];
  ultimoAviso: string[];
  cambioContraventor: string[];
  fuente: string[];
  conContraventor: TriEstado;
  pagado: TriEstado;
  conDp: TriEstado;
  /** Sentido de la respuesta del OT al DP (§7.1): Favorable, Desfavorable o SinRespuesta. */
  resultadoDp: "" | "Favorable" | "Desfavorable" | "SinRespuesta";
  activos: "activos" | "inactivos" | "todos";
  cobroCoactivo: TriEstado;
  sinGestion: boolean;
  rapido: FiltroRapido | null;
  juridico: FiltroJuridico | null;
  /** Los filtros de lista pedidos con «no es». */
  negar: CampoNegable[];
}

export const FILTROS_VACIOS: FiltrosBandeja = {
  buscar: "",
  nits: [],
  placas: [],
  numeros: [],
  organismos: [],
  imposicionDesde: "",
  imposicionHasta: "",
  notificacionDesde: "",
  notificacionHasta: "",
  registroDesde: "",
  registroHasta: "",
  estadoLegal: [],
  estadoProceso: [],
  ultimoAviso: [],
  cambioContraventor: [],
  fuente: [],
  conContraventor: "",
  pagado: "",
  conDp: "",
  resultadoDp: "",
  activos: "activos",
  cobroCoactivo: "",
  sinGestion: false,
  rapido: null,
  juridico: null,
  negar: [],
};

const NEGABLES: CampoNegable[] = ["nits", "placas", "numeros", "organismos", "estadoProceso", "ultimoAviso", "cambioContraventor", "fuente"];

/** Query de la API: solo lo que tiene valor; las listas viajan separadas por comas. */
export function aQuery(f: FiltrosBandeja, extra: Query = {}): Query {
  const lista = (v: string[]) => (v.length > 0 ? v.join(",") : undefined);
  const texto = (v: string) => (v.trim() ? v.trim() : undefined);
  return {
    buscar: texto(f.buscar),
    nits: lista(f.nits),
    placas: lista(f.placas),
    numeros: lista(f.numeros),
    organismos: lista(f.organismos),
    imposicionDesde: texto(f.imposicionDesde),
    imposicionHasta: texto(f.imposicionHasta),
    notificacionDesde: texto(f.notificacionDesde),
    notificacionHasta: texto(f.notificacionHasta),
    registroDesde: texto(f.registroDesde),
    registroHasta: texto(f.registroHasta),
    estadoLegal: lista(f.estadoLegal),
    estadoProceso: lista(f.estadoProceso),
    ultimoAviso: lista(f.ultimoAviso),
    cambioContraventor: lista(f.cambioContraventor),
    fuente: lista(f.fuente),
    conContraventor: f.conContraventor || undefined,
    pagado: f.pagado || undefined,
    conDp: f.conDp || undefined,
    resultadoDp: f.resultadoDp || undefined,
    activos: f.activos === "activos" ? undefined : f.activos,
    cobroCoactivo: f.cobroCoactivo || undefined,
    sinGestion: f.sinGestion ? true : undefined,
    rapido: f.rapido ?? undefined,
    juridico: f.juridico ?? undefined,
    // «no es» solo de las listas que tienen valores: sin valores no hay nada que negar.
    negar: lista(NEGABLES.filter((c) => f.negar.includes(c) && f[c].length > 0)),
    ...extra,
  };
}

/**
 * Cuántos filtros del menú «Filtros» están aplicados (para el botón «Filtros (N)»). La búsqueda, el estado legal y la
 * búsqueda rápida van aparte, y «Activos» no cuenta porque viene de fábrica; solo cuenta «Solo inactivos».
 */
export function contarFiltros(f: FiltrosBandeja): number {
  let n = 0;
  for (const v of [f.nits, f.placas, f.numeros, f.organismos, f.estadoProceso, f.ultimoAviso, f.cambioContraventor, f.fuente]) if (v.length) n++;
  for (const v of [f.conContraventor, f.pagado, f.conDp, f.resultadoDp, f.cobroCoactivo]) if (v) n++;
  if (f.imposicionDesde || f.imposicionHasta) n++;
  if (f.notificacionDesde || f.notificacionHasta) n++;
  if (f.registroDesde || f.registroHasta) n++;
  // «Activos e inactivos» es no filtrar: no cuenta, igual que no tiene etiqueta.
  if (f.activos === "inactivos") n++;
  return n;
}

/** Una lista pegada desde Excel (una por línea, o separadas por comas, punto y coma o espacios), sin repetidos. */
export function leerLista(texto: string): string[] {
  return [...new Set(texto.split(/[\s,;]+/).map((v) => v.trim().toUpperCase()).filter(Boolean))];
}

/** Lo que se ve en cada etiqueta de la tira y en el resumen de cada campo del menú. */
export type ClaveEtiqueta =
  | "rapido" | "juridico" | "sinGestion" | "estadoLegal" | "activos" | CampoNegable
  | "imposicion" | "notificacion" | "registro" | "conContraventor" | "pagado" | "conDp" | "resultadoDp" | "cobroCoactivo";

export interface EtiquetaFiltro {
  clave: ClaveEtiqueta;
  texto: string;
  /** «Activos»: viene puesto de fábrica (borde punteado), pero se quita igual que los demás. */
  porDefecto?: boolean;
}

export const NOMBRE_CAMPO: Record<ClaveEtiqueta, string> = {
  rapido: "Búsqueda rápida",
  juridico: "Filtro jurídico",
  sinGestion: "Sin gestión",
  estadoLegal: "Estado legal",
  activos: "Activos / inactivos",
  nits: "NIT del propietario",
  placas: "Placa",
  numeros: "Número de comparendo",
  organismos: "Organismo de tránsito",
  estadoProceso: "Estado del proceso",
  ultimoAviso: "Último aviso",
  cambioContraventor: "Cambio de contraventor",
  fuente: "Fuente",
  imposicion: "Fecha de imposición",
  notificacion: "Fecha de notificación",
  registro: "Fecha de registro",
  conContraventor: "Contraventor",
  pagado: "Pago",
  conDp: "Derecho de petición",
  resultadoDp: "Resultado del DP",
  cobroCoactivo: "Cobro coactivo",
};

export const TEXTO_RAPIDO: Record<FiltroRapido, string> = {
  ProximosAVencer: "Próximos a vencer",
  SinContraventor: "Sin contraventor (manual)",
  RechazadosOt: "Rechazados por OT",
  Radar: "Radar de vencimientos (7 días hábiles)",
};
export const TEXTO_JURIDICO: Record<FiltroJuridico, string> = { CaducidadInmediata: "Caducidad inmediata", NoImpugnables: "No impugnables", C038: "C-038" };
export const TEXTO_RESULTADO_DP: Record<string, string> = { Favorable: "Favorable", Desfavorable: "Desfavorable", SinRespuesta: "Enviado, sin respuesta" };
/** Lo que dice cada opción de los filtros de sí o no. */
export const TEXTO_TRI: Record<"conContraventor" | "pagado" | "conDp" | "cobroCoactivo", { si: string; no: string }> = {
  conContraventor: { si: "Con contraventor", no: "Sin contraventor" },
  pagado: { si: "Pagados", no: "Sin pago" },
  conDp: { si: "Con derecho de petición", no: "Sin derecho de petición" },
  cobroCoactivo: { si: "En cobro coactivo", no: "Sin cobro coactivo" },
};

/** Los valores de cada lista de estados, con su texto. */
export const OPCIONES_LISTA: Record<"estadoProceso" | "ultimoAviso" | "cambioContraventor" | "fuente", Record<string, Etiqueta>> = {
  estadoProceso: ESTADO_PROCESO,
  ultimoAviso: ULTIMO_AVISO,
  cambioContraventor: CAMBIO_CONTRAVENTOR,
  fuente: Object.fromEntries(Object.entries(FUENTE).filter(([k]) => k !== "MigracionV1")),
};

function rangoTexto(desde: string, hasta: string): string {
  if (desde && hasta) return `${formatoFecha(desde)} a ${formatoFecha(hasta)}`;
  return desde ? `desde ${formatoFecha(desde)}` : `hasta ${formatoFecha(hasta)}`;
}

/**
 * Las etiquetas de lo aplicado, en el orden de la pantalla: búsqueda rápida, estado legal, «Activos» y lo del menú.
 * `nombres` traduce los ids de NIT y organismo; con más de dos valores la etiqueta dice cuántos son.
 */
export function etiquetasAplicadas(f: FiltrosBandeja, nombres: Record<string, string> = {}): EtiquetaFiltro[] {
  const r: EtiquetaFiltro[] = [];
  const lista = (clave: CampoNegable, valores: string[], texto: (v: string) => string = (v) => nombres[v] ?? v) => {
    if (!valores.length) return;
    const cuales = valores.length > 2 ? `${valores.length} valores` : valores.map(texto).join(", ");
    r.push({ clave, texto: `${NOMBRE_CAMPO[clave]} ${f.negar.includes(clave) ? "no es" : "es"} ${cuales}` });
  };
  const de = (mapa: Record<string, Etiqueta>) => (v: string) => mapa[v]?.texto ?? v;
  if (f.rapido) r.push({ clave: "rapido", texto: TEXTO_RAPIDO[f.rapido] });
  if (f.juridico) r.push({ clave: "juridico", texto: TEXTO_JURIDICO[f.juridico] });
  if (f.sinGestion) r.push({ clave: "sinGestion", texto: "Sin gestión" });
  if (f.estadoLegal.length) r.push({ clave: "estadoLegal", texto: `Estado legal: ${f.estadoLegal.map(de(ESTADO_LEGAL)).join(", ")}` });
  if (f.activos === "activos") r.push({ clave: "activos", texto: "Activos", porDefecto: true });
  else if (f.activos === "inactivos") r.push({ clave: "activos", texto: "Solo inactivos" });
  lista("numeros", f.numeros);
  lista("organismos", f.organismos);
  lista("estadoProceso", f.estadoProceso, de(ESTADO_PROCESO));
  lista("fuente", f.fuente, de(FUENTE));
  if (f.cobroCoactivo) r.push({ clave: "cobroCoactivo", texto: TEXTO_TRI.cobroCoactivo[f.cobroCoactivo === "true" ? "si" : "no"] });
  lista("placas", f.placas);
  lista("nits", f.nits);
  for (const [clave, titulo, desde, hasta] of [
    ["imposicion", "Imposición", f.imposicionDesde, f.imposicionHasta],
    ["notificacion", "Notificación", f.notificacionDesde, f.notificacionHasta],
    ["registro", "Registro", f.registroDesde, f.registroHasta],
  ] as const) {
    if (desde || hasta) r.push({ clave, texto: `${titulo}: ${rangoTexto(desde, hasta)}` });
  }
  if (f.conContraventor) r.push({ clave: "conContraventor", texto: TEXTO_TRI.conContraventor[f.conContraventor === "true" ? "si" : "no"] });
  lista("cambioContraventor", f.cambioContraventor, de(CAMBIO_CONTRAVENTOR));
  lista("ultimoAviso", f.ultimoAviso, de(ULTIMO_AVISO));
  if (f.conDp) r.push({ clave: "conDp", texto: TEXTO_TRI.conDp[f.conDp === "true" ? "si" : "no"] });
  if (f.resultadoDp) r.push({ clave: "resultadoDp", texto: `Resultado del DP: ${TEXTO_RESULTADO_DP[f.resultadoDp]}` });
  if (f.pagado) r.push({ clave: "pagado", texto: TEXTO_TRI.pagado[f.pagado === "true" ? "si" : "no"] });
  return r;
}

/** Quita el filtro de una etiqueta. Quitar «Activos» deja ver activos e inactivos. */
export function quitarEtiqueta(f: FiltrosBandeja, clave: ClaveEtiqueta): FiltrosBandeja {
  const sinNegar = (c: CampoNegable) => f.negar.filter((x) => x !== c);
  switch (clave) {
    case "rapido":
      return { ...f, rapido: null };
    case "juridico":
      return { ...f, juridico: null };
    case "sinGestion":
      return { ...f, sinGestion: false };
    case "estadoLegal":
      return { ...f, estadoLegal: [] };
    case "activos":
      return { ...f, activos: "todos" };
    case "imposicion":
      return { ...f, imposicionDesde: "", imposicionHasta: "" };
    case "notificacion":
      return { ...f, notificacionDesde: "", notificacionHasta: "" };
    case "registro":
      return { ...f, registroDesde: "", registroHasta: "" };
    case "conContraventor":
    case "pagado":
    case "conDp":
    case "resultadoDp":
    case "cobroCoactivo":
      return { ...f, [clave]: "" };
    default:
      return { ...f, [clave]: [], negar: sinNegar(clave) };
  }
}

/** «Mostrando 26–50 de 300» del pie de las tablas paginadas en servidor. */
export function resumenPagina(pagina: number, tamano: number, mostradas: number, total: number, sustantivo = "registros"): string {
  if (total === 0) return `Sin ${sustantivo} que mostrar`;
  const desde = (pagina - 1) * tamano + 1;
  return `Mostrando ${desde}–${desde + mostradas - 1} de ${total}`;
}

/** Lo que la bandeja guarda en su URL además de los filtros: al volver del detalle queda igual (rediseño S1). */
export interface VistaBandeja {
  orden: string;
  descendente: boolean;
  pagina: number;
  tamano: number;
  /** Empresa elegida por el SuperAdmin. */
  empresa: string | null;
}

export const VISTA_POR_DEFECTO: VistaBandeja = { orden: "", descendente: false, pagina: 1, tamano: 50, empresa: null };

const ORDENES_VALIDOS = ["valor", "imposicion", "notificacion", "actividad", "numero"];
const TAMANOS_VALIDOS = [10, 20, 30, 50];
const RAPIDOS: FiltroRapido[] = ["ProximosAVencer", "SinContraventor", "RechazadosOt", "Radar"];
const JURIDICOS: FiltroJuridico[] = ["CaducidadInmediata", "NoImpugnables", "C038"];
const RESULTADOS_DP = ["Favorable", "Desfavorable", "SinRespuesta"] as const;
const FECHA = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Filtros y vista de la bandeja como parámetros de la URL: solo lo que difiere de fábrica, con las mismas claves que la
 * query de la API. Una bandeja recién abierta deja la URL limpia.
 */
export function filtrosAParams(f: FiltrosBandeja, vista: VistaBandeja = VISTA_POR_DEFECTO): URLSearchParams {
  const p = new URLSearchParams();
  for (const [clave, valor] of Object.entries(aQuery(f))) {
    if (valor !== undefined && valor !== null && valor !== "") p.set(clave, String(valor));
  }
  if (vista.empresa) p.set("empresa", vista.empresa);
  if (vista.orden) p.set("orden", vista.orden);
  if (vista.orden && vista.descendente) p.set("descendente", "true");
  if (vista.pagina > 1) p.set("pagina", String(vista.pagina));
  if (vista.tamano !== VISTA_POR_DEFECTO.tamano) p.set("tamano", String(vista.tamano));
  return p;
}

/** Lector mínimo de la URL (sirve igual con `URLSearchParams` y con `useSearchParams` de Next). */
export interface LectorParams {
  get(clave: string): string | null;
}

/**
 * La inversa de {@link filtrosAParams}. Lo que no se reconoce se ignora (un enlace viejo o editado a mano no rompe la
 * bandeja). Acepta también lo que mandan el dashboard y Reportes: `?nit=` (uno solo) y `?rapido=Radar`.
 */
export function filtrosDeParams(p: LectorParams): { filtros: FiltrosBandeja; vista: VistaBandeja } {
  const lista = (clave: string) => (p.get(clave) ?? "").split(",").map((v) => v.trim()).filter(Boolean);
  const deCatalogo = (clave: string, catalogo: Record<string, unknown>) => lista(clave).filter((v) => v in catalogo);
  const uno = <T extends string>(clave: string, validos: readonly T[]): T | null => {
    const v = p.get(clave);
    return v && (validos as readonly string[]).includes(v) ? (v as T) : null;
  };
  const tri = (clave: string): TriEstado => uno(clave, ["true", "false"] as const) ?? "";
  const fecha = (clave: string) => {
    const v = p.get(clave) ?? "";
    return FECHA.test(v) ? v : "";
  };
  const nitSuelto = p.get("nit");
  const activos = uno("activos", ["inactivos", "todos"] as const) ?? "activos";
  const filtros: FiltrosBandeja = {
    buscar: (p.get("buscar") ?? "").trim(),
    nits: [...new Set([...lista("nits"), ...(nitSuelto ? [nitSuelto] : [])])],
    placas: lista("placas"),
    numeros: lista("numeros"),
    organismos: lista("organismos"),
    imposicionDesde: fecha("imposicionDesde"),
    imposicionHasta: fecha("imposicionHasta"),
    notificacionDesde: fecha("notificacionDesde"),
    notificacionHasta: fecha("notificacionHasta"),
    registroDesde: fecha("registroDesde"),
    registroHasta: fecha("registroHasta"),
    estadoLegal: deCatalogo("estadoLegal", ESTADO_LEGAL),
    estadoProceso: deCatalogo("estadoProceso", ESTADO_PROCESO),
    ultimoAviso: deCatalogo("ultimoAviso", ULTIMO_AVISO),
    cambioContraventor: deCatalogo("cambioContraventor", CAMBIO_CONTRAVENTOR),
    fuente: deCatalogo("fuente", FUENTE),
    conContraventor: tri("conContraventor"),
    pagado: tri("pagado"),
    conDp: tri("conDp"),
    resultadoDp: uno("resultadoDp", RESULTADOS_DP) ?? "",
    activos,
    cobroCoactivo: tri("cobroCoactivo"),
    sinGestion: p.get("sinGestion") === "true",
    rapido: uno("rapido", RAPIDOS),
    juridico: uno("juridico", JURIDICOS),
    negar: lista("negar").filter((c): c is CampoNegable => (NEGABLES as string[]).includes(c)),
  };
  const pagina = Number(p.get("pagina"));
  const tamano = Number(p.get("tamano"));
  const orden = uno("orden", ORDENES_VALIDOS) ?? "";
  const vista: VistaBandeja = {
    orden,
    descendente: !!orden && p.get("descendente") === "true",
    pagina: Number.isInteger(pagina) && pagina > 1 ? pagina : 1,
    tamano: TAMANOS_VALIDOS.includes(tamano) ? tamano : VISTA_POR_DEFECTO.tamano,
    empresa: p.get("empresa") || null,
  };
  return { filtros, vista };
}
