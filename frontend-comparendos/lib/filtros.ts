// Filtros de la bandeja (§7.1) y su traducción a la query de GET /comparendos. Lógica pura y probada.
import type { Query } from "@/lib/api";

export type FiltroRapido = "ProximosAVencer" | "SinContraventor" | "RechazadosOt";
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
  for (const v of [f.placa, f.numero, f.conContraventor, f.pagado, f.conDp, f.cobroCoactivo]) if (v.trim()) n++;
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
