// Grilla de placas de la compañía (pestaña «Vehículos» del inicio, PS-3; plan §6.6, HU #14577). Tipos y llamada de
// GET /vehiculos en snake_case, y filtros en la URL al estilo de lib/diagnosticos/listado.ts. La compañía es la del token.
// Correcciones del brief de la Fase 0: el estado del vehículo es EN_PROCESO o TERMINADO, calculado con sus celdas (C1), y
// el modo es el del contrato vigente, SIMPLE o CON_ANALISIS (C2). D-10: la pantalla filtra por un valor de cada lista
// aunque la API acepte repetibles.
import { ApiError, apiFetch } from "@/lib/api";
import type { ModoDiagnostico } from "@/lib/diagnosticos/api";
import { MODOS } from "@/lib/diagnosticos/listado";
import type { EstadoSaneamientoActivo, SaneamientoFila } from "@/lib/saneamiento/api";
import type { TramiteFila } from "@/lib/tablero/api";

export type EstadoVehiculo = "EN_PROCESO" | "TERMINADO";
export type VeredictoFiltro = "VIABLE" | "BLOQUEADO" | "SIN_DICTAMEN";
export type FiltroSaneamiento = "CON" | "SIN";

export interface FilaVehiculoListado {
  vehiculo_id: string;
  placa: string | null;
  diagnostico_id: string;
  diagnostico_nombre: string;
  diagnostico_creado_en: string;
  modo: ModoDiagnostico;
  estado: EstadoVehiculo;
  /** null: sin dictamen (modo Simple o pendiente). */
  veredicto: "VIABLE" | "BLOQUEADO" | null;
  saneamiento: SaneamientoFila;
  tramite: Pick<TramiteFila, "id" | "numero"> | null;
}

export interface ListaVehiculos {
  pagina: number;
  tamano: number;
  /** Todas las filas visibles que cumplen los filtros, en todas las páginas. */
  total: number;
  vehiculos: FilaVehiculoListado[];
}

export interface FiltrosVehiculos {
  /** Prefijo de la placa; la consulta lo manda recortado y en mayúsculas. */
  placa: string;
  estado: EstadoVehiculo | null;
  veredicto: VeredictoFiltro | null;
  saneamiento: FiltroSaneamiento | null;
  /** Implica `saneamiento = CON`. */
  saneamientoEstado: EstadoSaneamientoActivo | null;
  modo: ModoDiagnostico | null;
  conTramite: boolean | null;
  /** AAAA-MM-DD (fecha de creación del diagnóstico) o "". */
  desde: string;
  hasta: string;
  pagina: number;
  tamano: number;
}

export type CampoFiltroVehiculos = "placa" | "estado" | "veredicto" | "saneamiento" | "saneamiento_estado" | "modo" | "con_tramite" | "desde" | "hasta";

export type ErrorVehiculos =
  | { tipo: "CAMPO"; campo: CampoFiltroVehiculos; mensaje: string }
  | { tipo: "ACCESO"; code: string }
  | { tipo: "AVISO"; mensaje: string; reiniciarPagina: boolean };

/** PS-3 (spec-ui §4.3): 25 por defecto, opciones 25/50/100 (el contrato admite hasta 100). */
export const TAMANO_VEHICULOS_POR_DEFECTO = 25;
export const TAMANOS_VEHICULOS: readonly number[] = [25, 50, 100];

export const ESTADOS_VEHICULO: readonly EstadoVehiculo[] = ["EN_PROCESO", "TERMINADO"];
export const VEREDICTOS_FILTRO: readonly VeredictoFiltro[] = ["VIABLE", "BLOQUEADO", "SIN_DICTAMEN"];
export const ESTADOS_SANEAMIENTO_FILTRO: readonly EstadoSaneamientoActivo[] = ["SOLICITADO", "EN_GESTION", "SANEADO", "NO_PROCEDE"];

const FECHA = /^\d{4}-\d{2}-\d{2}$/;

const vacios = (): FiltrosVehiculos => ({
  placa: "",
  estado: null,
  veredicto: null,
  saneamiento: null,
  saneamientoEstado: null,
  modo: null,
  conTramite: null,
  desde: "",
  hasta: "",
  pagina: 1,
  tamano: TAMANO_VEHICULOS_POR_DEFECTO,
});

function entero(valor: string | null): number | null {
  if (valor === null || !/^\d+$/.test(valor)) return null;
  return Number.parseInt(valor, 10);
}

function uno<T extends string>(valor: string | null, validos: readonly T[]): T | null {
  return validos.includes(valor as T) ? (valor as T) : null;
}

/** Filtros de la URL del inicio (pestaña Vehículos). Lo que la API no admite se descarta. */
export function filtrosVehiculosDesdeUrl(params: URLSearchParams): FiltrosVehiculos {
  const f = vacios();
  f.placa = params.get("placa") ?? "";
  f.estado = uno(params.get("estado"), ESTADOS_VEHICULO);
  f.veredicto = uno(params.get("veredicto"), VEREDICTOS_FILTRO);
  f.saneamiento = uno<FiltroSaneamiento>(params.get("saneamiento"), ["CON", "SIN"]);
  f.saneamientoEstado = uno(params.get("saneamiento_estado"), ESTADOS_SANEAMIENTO_FILTRO);
  if (f.saneamientoEstado) f.saneamiento = "CON";
  f.modo = uno(params.get("modo"), MODOS.map((m) => m.valor));
  const tramite = params.get("con_tramite");
  f.conTramite = tramite === "true" ? true : tramite === "false" ? false : null;
  const desde = params.get("desde") ?? "";
  const hasta = params.get("hasta") ?? "";
  if (FECHA.test(desde)) f.desde = desde;
  if (FECHA.test(hasta)) f.hasta = hasta;
  const pagina = entero(params.get("pagina"));
  if (pagina !== null && pagina >= 1) f.pagina = pagina;
  const tamano = entero(params.get("tamano"));
  if (tamano !== null && TAMANOS_VEHICULOS.includes(tamano)) f.tamano = tamano;
  return f;
}

function parametros(f: FiltrosVehiculos, normalizar: boolean): URLSearchParams {
  const q = new URLSearchParams();
  const placa = f.placa.trim();
  if (placa) q.set("placa", normalizar ? placa.toUpperCase() : placa);
  if (f.estado) q.set("estado", f.estado);
  if (f.veredicto) q.set("veredicto", f.veredicto);
  const saneamiento = f.saneamientoEstado ? "CON" : f.saneamiento;
  if (saneamiento) q.set("saneamiento", saneamiento);
  if (f.saneamientoEstado) q.set("saneamiento_estado", f.saneamientoEstado);
  if (f.modo) q.set("modo", f.modo);
  if (f.conTramite !== null) q.set("con_tramite", String(f.conTramite));
  if (f.desde) q.set("desde", f.desde);
  if (f.hasta) q.set("hasta", f.hasta);
  return q;
}

/** Consulta de GET /vehiculos: sin vacíos, placa en mayúsculas, siempre con página y tamaño. */
export function consultaVehiculos(f: FiltrosVehiculos): string {
  const q = parametros(f, true);
  q.set("pagina", String(f.pagina));
  q.set("tamano", String(f.tamano));
  return q.toString();
}

/** URL del inicio en la pestaña Vehículos con los filtros: omite la página 1 y el tamaño por defecto. */
export function urlVehiculos(f: FiltrosVehiculos): string {
  const q = new URLSearchParams({ vista: "vehiculos" });
  for (const [clave, valor] of parametros(f, false)) q.append(clave, valor);
  if (f.pagina > 1) q.set("pagina", String(f.pagina));
  if (f.tamano !== TAMANO_VEHICULOS_POR_DEFECTO) q.set("tamano", String(f.tamano));
  return `/?${q.toString()}`;
}

/** Algún filtro activo (página y tamaño no cuentan): separa «ningún vehículo coincide» de «aún no hay vehículos». */
export function hayFiltrosVehiculos(f: FiltrosVehiculos): boolean {
  return Boolean(f.placa.trim() || f.estado || f.veredicto || f.saneamiento || f.saneamientoEstado || f.modo || f.conTramite !== null || f.desde || f.hasta);
}

/** GET /vehiculos (permiso diagnostico.diagnosticos.ver), orden: creación del diagnóstico desc y placa. */
export async function listarVehiculos(f: FiltrosVehiculos, signal?: AbortSignal): Promise<ListaVehiculos> {
  const respuesta = await apiFetch<ListaVehiculos | null>(`/vehiculos?${consultaVehiculos(f)}`, { signal });
  if (!respuesta || !Array.isArray(respuesta.vehiculos)) throw new Error("Diagnóstico respondió algo inesperado. Intenta de nuevo en un momento.");
  return respuesta;
}

const MENSAJES_CAMPO: Record<CampoFiltroVehiculos, string> = {
  placa: "Revisa la placa.",
  estado: "Elige un estado de la lista.",
  veredicto: "Elige un dictamen de la lista.",
  saneamiento: "Elige una opción de saneamiento de la lista.",
  saneamiento_estado: "Elige una opción de saneamiento de la lista.",
  modo: "Elige un modo de la lista.",
  con_tramite: "Elige una opción de trámite de la lista.",
  desde: "Escribe la fecha inicial como día/mes/año.",
  hasta: "Escribe la fecha final como día/mes/año.",
};

/** Código de la 403 cuando la API niega sin cuerpo, como el listado de diagnósticos. */
const SIN_PERMISOS = "SIN_PERMISOS_DIAGNOSTICO";

/** 403 → acceso negado; 400 con campo de filtro → ese control; PAGINACION_INVALIDA → reiniciar; el resto → aviso. */
export function traducirErrorVehiculos(error: unknown): ErrorVehiculos {
  if (error instanceof ApiError) {
    if (error.status === 403) return { tipo: "ACCESO", code: error.code || SIN_PERMISOS };
    if (error.status === 400 && error.code === "PAGINACION_INVALIDA")
      return { tipo: "AVISO", mensaje: "La página pedida no es válida. Vuelve a la primera página.", reiniciarPagina: true };
    if (error.status === 400 && error.campo && Object.prototype.hasOwnProperty.call(MENSAJES_CAMPO, error.campo)) {
      const campo = error.campo as CampoFiltroVehiculos;
      const mensaje = error.code === "RANGO_INVALIDO" ? "La fecha final no puede ser anterior a la inicial." : MENSAJES_CAMPO[campo];
      return { tipo: "CAMPO", campo, mensaje };
    }
    return { tipo: "AVISO", mensaje: error.message, reiniciarPagina: false };
  }
  return { tipo: "AVISO", mensaje: error instanceof Error ? error.message : "No pudimos cargar los vehículos.", reiniciarPagina: false };
}
