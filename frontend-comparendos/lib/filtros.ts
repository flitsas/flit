// Filtros de la bandeja (§7.1) y su traducción a la query de GET /comparendos. Lógica pura y probada.
import type { Query } from "@/lib/api";

/** «Radar» no está en la barra: llega desde el KPI del dashboard (§7.8). */
export type FiltroRapido = "ProximosAVencer" | "SinContraventor" | "RechazadosOt" | "Radar";
export type FiltroJuridico = "CaducidadInmediata" | "NoImpugnables" | "C038";
export type TriEstado = "" | "true" | "false";

export interface FiltrosBandeja {
  nits: string[];
  placa: string;
  numero: string;
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
}

export const FILTROS_VACIOS: FiltrosBandeja = {
  nits: [],
  placa: "",
  numero: "",
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
};

/** Query de la API: solo lo que tiene valor; las listas viajan separadas por comas. */
export function aQuery(f: FiltrosBandeja, extra: Query = {}): Query {
  const lista = (v: string[]) => (v.length > 0 ? v.join(",") : undefined);
  const texto = (v: string) => (v.trim() ? v.trim() : undefined);
  return {
    nits: lista(f.nits),
    placa: texto(f.placa),
    numero: texto(f.numero),
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
    ...extra,
  };
}

/** Cuántos filtros del panel están aplicados (para el botón «Filtros (N)»); rápidos y jurídicos van aparte. */
export function contarFiltros(f: FiltrosBandeja): number {
  let n = 0;
  for (const v of [f.nits, f.organismos, f.estadoLegal, f.estadoProceso, f.ultimoAviso, f.cambioContraventor, f.fuente]) if (v.length) n++;
  for (const v of [f.placa, f.numero, f.conContraventor, f.pagado, f.conDp, f.resultadoDp, f.cobroCoactivo]) if (v.trim()) n++;
  if (f.imposicionDesde || f.imposicionHasta) n++;
  if (f.notificacionDesde || f.notificacionHasta) n++;
  if (f.registroDesde || f.registroHasta) n++;
  if (f.activos !== "activos") n++;
  return n;
}

/** «Mostrando 26–50 de 300» del pie de las tablas paginadas en servidor. */
export function resumenPagina(pagina: number, tamano: number, mostradas: number, total: number, sustantivo = "registros"): string {
  if (total === 0) return `Sin ${sustantivo} que mostrar`;
  const desde = (pagina - 1) * tamano + 1;
  return `Mostrando ${desde}–${desde + mostradas - 1} de ${total}`;
}

/** Los filtros tal como se guardan con una exportación (FiltrosBandeja del backend, en camelCase). */
export interface FiltrosGuardados {
  nits?: string[] | null;
  placa?: string | null;
  numero?: string | null;
  organismos?: string[] | null;
  imposicionDesde?: string | null;
  imposicionHasta?: string | null;
  notificacionDesde?: string | null;
  notificacionHasta?: string | null;
  registroDesde?: string | null;
  registroHasta?: string | null;
  estadoLegal?: string[] | null;
  estadoProceso?: string[] | null;
  ultimoAviso?: string[] | null;
  cambioContraventor?: string[] | null;
  fuente?: string[] | null;
  conContraventor?: boolean | null;
  pagado?: boolean | null;
  conDp?: boolean | null;
  resultadoDp?: string | null;
  activos?: string | null;
  cobroCoactivo?: boolean | null;
  sinGestion?: boolean | null;
  rapido?: string | null;
  juridico?: string | null;
}

const RAPIDO_TEXTO: Record<string, string> = {
  ProximosAVencer: "Próximos a vencer",
  SinContraventor: "Sin contraventor",
  RechazadosOt: "Rechazados por OT",
  Radar: "Radar de vencimientos",
};
const JURIDICO_TEXTO: Record<string, string> = { CaducidadInmediata: "Caducidad inmediata", NoImpugnables: "No impugnables", C038: "C-038" };
const RESULTADO_DP_TEXTO: Record<string, string> = { Favorable: "Favorable", Desfavorable: "Desfavorable", SinRespuesta: "Enviado, sin respuesta" };

/**
 * «Mis exportaciones» (§7.26): los filtros con que se pidió una exportación, en texto. `etiquetas` traduce los valores de
 * cada lista (estado legal, proceso…) y `nombres` los ids de NIT y organismo. Sin filtros: «Sin filtros».
 */
export function describirFiltros(
  f: FiltrosGuardados,
  nombres: Record<string, string>,
  etiquetas: Partial<Record<"estadoLegal" | "estadoProceso" | "ultimoAviso" | "cambioContraventor" | "fuente", Record<string, { texto: string }>>>,
): string {
  const partes: string[] = [];
  const lista = (titulo: string, v: string[] | null | undefined, mapa?: Record<string, { texto: string }>) => {
    if (v?.length) partes.push(`${titulo}: ${v.map((x) => mapa?.[x]?.texto ?? nombres[x] ?? x).join(", ")}`);
  };
  const rango = (titulo: string, desde?: string | null, hasta?: string | null) => {
    if (desde || hasta) partes.push(`${titulo}: ${desde ?? "…"} a ${hasta ?? "…"}`);
  };
  const si = (v: boolean | null | undefined, verdadero: string, falso: string) => {
    if (v === true) partes.push(verdadero);
    else if (v === false) partes.push(falso);
  };
  if (f.rapido) partes.push(RAPIDO_TEXTO[f.rapido] ?? f.rapido);
  if (f.juridico) partes.push(JURIDICO_TEXTO[f.juridico] ?? f.juridico);
  lista("NIT", f.nits);
  if (f.placa?.trim()) partes.push(`Placa: ${f.placa.trim().toUpperCase()}`);
  if (f.numero?.trim()) partes.push(`Número: ${f.numero.trim()}`);
  lista("Organismo", f.organismos);
  rango("Imposición", f.imposicionDesde, f.imposicionHasta);
  rango("Notificación", f.notificacionDesde, f.notificacionHasta);
  rango("Registro", f.registroDesde, f.registroHasta);
  lista("Estado legal", f.estadoLegal, etiquetas.estadoLegal);
  lista("Estado del proceso", f.estadoProceso, etiquetas.estadoProceso);
  lista("Último aviso", f.ultimoAviso, etiquetas.ultimoAviso);
  lista("Cambio de contraventor", f.cambioContraventor, etiquetas.cambioContraventor);
  lista("Fuente", f.fuente, etiquetas.fuente);
  si(f.conContraventor, "Con contraventor", "Sin contraventor");
  si(f.pagado, "Pagados", "Sin pagar");
  si(f.conDp, "Con DP", "Sin DP");
  if (f.resultadoDp) partes.push(`Resultado del DP: ${RESULTADO_DP_TEXTO[f.resultadoDp] ?? f.resultadoDp}`);
  si(f.cobroCoactivo, "En cobro coactivo", "Sin cobro coactivo");
  if (f.sinGestion) partes.push("Sin gestión");
  if (f.activos === "inactivos") partes.push("Solo inactivos");
  else if (f.activos === "todos") partes.push("Activos e inactivos");
  return partes.length ? partes.join(" · ") : "Sin filtros";
}
