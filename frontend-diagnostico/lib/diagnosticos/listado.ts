// Listado de diagnósticos de la compañía en el inicio (HU #13852, F08-H2). Tipos y llamada del contrato de HU #13845
// (operationId listarDiagnosticos en contracts/openapi/diagnostico.v1.yaml, schemas ListaDiagnosticos y DiagnosticoListado),
// en snake_case. La compañía es siempre la del token: nunca viaja en la consulta. Los filtros viven en la URL del inicio
// (?desde=&hasta=&modo=&estado=&nombre=&pagina=) para compartirla y volver atrás.
import { ApiError, apiFetch } from "@/lib/api";
import type { EstadoDiagnostico, ModoDiagnostico } from "@/lib/diagnosticos/api";

/** CREADO no es filtrable: los lotes sin lanzar solo salen sin filtro de estado (400 FILTRO_INVALIDO si se pide). */
export type EstadoFiltrable = Exclude<EstadoDiagnostico, "CREADO">;

export interface DiagnosticoListado {
  id: string;
  nombre: string;
  creado_en: string;
  modo: ModoDiagnostico;
  /** null en SIMPLE; si el código ya no está publicado, `nombre` es el código. */
  tipo_tramite: { codigo: string; nombre: string } | null;
  estado: EstadoDiagnostico;
  /** `nombre` llega «No disponible» si Identidad no responde: se muestra tal cual. */
  usuario_creador: { id: string; nombre: string };
}

export interface ListaDiagnosticos {
  pagina: number;
  tamano: number;
  /** Todos los visibles que cumplen los filtros, en todas las páginas. */
  total: number;
  diagnosticos: DiagnosticoListado[];
}

export interface FiltrosListado {
  /** AAAA-MM-DD (día de Colombia, inclusivo) o "". */
  desde: string;
  hasta: string;
  modo: ModoDiagnostico | null;
  estado: EstadoFiltrable | null;
  nombre: string;
  pagina: number;
  tamano: number;
}

export type CampoFiltro = "desde" | "hasta" | "modo" | "estado" | "nombre";

export type ErrorListado =
  /** 400 con `campo` de un filtro: el mensaje va junto a su control. */
  | { tipo: "CAMPO"; campo: CampoFiltro; mensaje: string }
  /** 403: la página de acceso negado con ese código. */
  | { tipo: "ACCESO"; code: string }
  /** El resto: aviso con Reintentar (PAGINACION_INVALIDA reintenta desde la página 1). */
  | { tipo: "AVISO"; mensaje: string; reiniciarPagina: boolean };

export const TAMANO_POR_DEFECTO = 25;
export const TAMANOS_LISTADO = [10, 25, 50, 100] as const;
/** maxLength del filtro nombre en el contrato. */
export const NOMBRE_FILTRO_MAXIMO = 100;

export const MODOS: ReadonlyArray<{ valor: ModoDiagnostico; nombre: string }> = [
  { valor: "SIMPLE", nombre: "Diagnóstico Simple" },
  { valor: "CON_ANALISIS", nombre: "Con Análisis IA" },
];

export const ESTADOS_FILTRABLES: readonly EstadoFiltrable[] = ["EN_PROCESO", "COMPLETADO", "INCOMPLETO"];

const ETIQUETAS_ESTADO: Record<EstadoDiagnostico, { texto: string; tono: "neutral" | "info" | "success" | "warning" }> = {
  CREADO: { texto: "Sin lanzar", tono: "neutral" },
  EN_PROCESO: { texto: "En proceso", tono: "info" },
  COMPLETADO: { texto: "Completado", tono: "success" },
  INCOMPLETO: { texto: "Incompleto", tono: "warning" },
};

const FECHA = /^\d{4}-\d{2}-\d{2}$/;

const vacios = (): FiltrosListado => ({ desde: "", hasta: "", modo: null, estado: null, nombre: "", pagina: 1, tamano: TAMANO_POR_DEFECTO });

function entero(valor: string | null): number | null {
  if (valor === null || !/^\d+$/.test(valor)) return null;
  return Number.parseInt(valor, 10);
}

/** Filtros de la URL del inicio. Lo que la API no admite (CREADO, minúsculas, fechas mal formadas, páginas no válidas) se descarta. */
export function filtrosDesdeUrl(params: URLSearchParams): FiltrosListado {
  const f = vacios();
  const desde = params.get("desde") ?? "";
  const hasta = params.get("hasta") ?? "";
  if (FECHA.test(desde)) f.desde = desde;
  if (FECHA.test(hasta)) f.hasta = hasta;
  const modo = params.get("modo");
  if (MODOS.some((m) => m.valor === modo)) f.modo = modo as ModoDiagnostico;
  const estado = params.get("estado");
  if (ESTADOS_FILTRABLES.includes(estado as EstadoFiltrable)) f.estado = estado as EstadoFiltrable;
  f.nombre = params.get("nombre") ?? "";
  const pagina = entero(params.get("pagina"));
  if (pagina !== null && pagina >= 1) f.pagina = pagina;
  const tamano = entero(params.get("tamano"));
  if (tamano !== null && tamano >= 1 && tamano <= 100) f.tamano = tamano;
  return f;
}

function parametros(f: FiltrosListado): URLSearchParams {
  const q = new URLSearchParams();
  if (f.desde) q.set("desde", f.desde);
  if (f.hasta) q.set("hasta", f.hasta);
  if (f.modo) q.set("modo", f.modo);
  if (f.estado) q.set("estado", f.estado);
  const nombre = f.nombre.trim();
  if (nombre) q.set("nombre", nombre);
  return q;
}

/** Consulta de GET /diagnosticos: sin los filtros vacíos, con el nombre recortado y siempre con página y tamaño. */
export function consultaListado(f: FiltrosListado): string {
  const q = parametros(f);
  q.set("pagina", String(f.pagina));
  q.set("tamano", String(f.tamano));
  return q.toString();
}

/** URL del inicio con los filtros: omite la página 1 y el tamaño por defecto. */
export function urlListado(f: FiltrosListado): string {
  const q = parametros(f);
  if (f.pagina > 1) q.set("pagina", String(f.pagina));
  if (f.tamano !== TAMANO_POR_DEFECTO) q.set("tamano", String(f.tamano));
  const texto = q.toString();
  return texto ? `/?${texto}` : "/";
}

/** Algún filtro activo (la página y el tamaño no cuentan): separa «sin resultados» de «compañía sin diagnósticos». */
export function hayFiltros(f: FiltrosListado): boolean {
  return Boolean(f.desde || f.hasta || f.modo || f.estado || f.nombre.trim());
}

/** GET /diagnosticos (permiso diagnostico.diagnosticos.ver), orden creado_en desc. */
export async function listarDiagnosticos(f: FiltrosListado, signal?: AbortSignal): Promise<ListaDiagnosticos> {
  const respuesta = await apiFetch<ListaDiagnosticos | null>(`/diagnosticos?${consultaListado(f)}`, { signal });
  if (!respuesta || !Array.isArray(respuesta.diagnosticos)) throw new Error("Diagnóstico respondió algo inesperado. Intenta de nuevo en un momento.");
  return respuesta;
}

const CAMPOS: ReadonlySet<string> = new Set<CampoFiltro>(["desde", "hasta", "modo", "estado", "nombre"]);

const MENSAJES_CAMPO: Record<CampoFiltro, string> = {
  desde: "Escribe la fecha inicial como día/mes/año.",
  hasta: "Escribe la fecha final como día/mes/año.",
  modo: "Elige un tipo de la lista.",
  estado: "Elige un estado de la lista.",
  nombre: `El nombre admite hasta ${NOMBRE_FILTRO_MAXIMO} caracteres.`,
};

/** Código de la 403 cuando la API niega sin cuerpo (usuario sin diagnostico.diagnosticos.ver), como DiagnosticoShell. */
const SIN_PERMISOS = "SIN_PERMISOS_DIAGNOSTICO";

/** 400 con campo de un filtro → ese control; 403 → página de acceso negado; el resto → aviso con Reintentar. */
export function traducirErrorListado(error: unknown): ErrorListado {
  if (error instanceof ApiError) {
    if (error.status === 403) return { tipo: "ACCESO", code: error.code || SIN_PERMISOS };
    if (error.status === 400 && error.code === "PAGINACION_INVALIDA")
      return { tipo: "AVISO", mensaje: "La página pedida no es válida. Vuelve a la primera página.", reiniciarPagina: true };
    if (error.status === 400 && error.campo && CAMPOS.has(error.campo)) {
      const campo = error.campo as CampoFiltro;
      const mensaje = error.code === "RANGO_INVALIDO" ? "La fecha final no puede ser anterior a la inicial." : MENSAJES_CAMPO[campo];
      return { tipo: "CAMPO", campo, mensaje };
    }
    return { tipo: "AVISO", mensaje: error.message, reiniciarPagina: false };
  }
  return { tipo: "AVISO", mensaje: error instanceof Error ? error.message : "No se pudo cargar el listado.", reiniciarPagina: false };
}

/** Un lote lanzado abre su tablero; uno sin lanzar (CREADO) sigue su parametrización en la carga de vehículos. */
export function destinoDiagnostico(d: Pick<DiagnosticoListado, "id" | "estado">): string {
  const base = `/diagnosticos/${encodeURIComponent(d.id)}`;
  return d.estado === "CREADO" ? `${base}/carga` : base;
}

/** Primeros 8 caracteres del id, en mayúsculas (el id completo queda en el title). */
export function idCorto(id: string): string {
  return id.slice(0, 8).toUpperCase();
}

export function nombreModo(modo: ModoDiagnostico): string {
  return MODOS.find((m) => m.valor === modo)?.nombre ?? modo;
}

/** Chip del estado en el listado: gris sin lanzar, azul en proceso, verde completado, ámbar incompleto. */
export function etiquetaEstado(estado: EstadoDiagnostico): { texto: string; tono: "neutral" | "info" | "success" | "warning" } {
  return ETIQUETAS_ESTADO[estado] ?? { texto: estado, tono: "neutral" };
}
