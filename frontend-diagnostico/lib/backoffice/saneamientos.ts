// Saneamientos del backoffice FLIT (plan §6.2, PS-4 y PS-5; HU #14577). Tipos y llamadas a mano, en snake_case.
// Grupo A, entre compañías (la empresa es un filtro opcional): bandeja, resumen de la insignia, Excel y «Tomar» varios.
// Grupo B, con `?empresa=` explícito (patrón de lib/backoffice/api.ts): detalle, deuda, transiciones, renglones y
// evidencias; sin una compañía válida no sale ninguna petición (EmpresaNoElegida).
// SP-15: el saneamiento de FLIT es un tipo propio (historial con usuario_id), separado del que ve la compañía. SP-7: la
// deuda de la gestión tiene su propia ruta del backoffice, auditada. SP-2: los renglones quitados no vuelven.
import { TAMANO_DE_PAGINA_POR_DEFECTO, TAMANOS_DE_PAGINA } from "@flit/ui/usePaginacion";
import { API_PREFIX, ApiError, apiFetch, type RequestOptions } from "@/lib/api";
import { EmpresaNoElegida } from "@/lib/backoffice/api";
import { esEmpresaValida } from "@/lib/configuracion";
import type { ModoDiagnostico } from "@/lib/diagnosticos/api";
import type {
  EstadoSaneamiento,
  EventoHistorial,
  PanelDeuda,
  RenglonSaneamiento,
  ResultadoRenglon,
} from "@/lib/saneamiento/api";

// ---------- Tipos ----------

/** Saneamiento de una fila de la grilla del backoffice (plan §6.1): estado e id para enlazar a PS-5. */
export interface SaneamientoFilaBackoffice {
  estado: EstadoSaneamiento | null;
  id: string | null;
}

export interface FilaBandeja {
  id: string;
  /** uuid de la compañía; el nombre sale de la lista del selector (D-12). */
  empresa: string;
  diagnostico_id: string;
  diagnostico_nombre: string;
  vehiculo_id: string;
  placa: string | null;
  modo: ModoDiagnostico;
  veredicto: "VIABLE" | "BLOQUEADO" | null;
  estado: EstadoSaneamiento;
  solicitado_en: string;
  tomado_en: string | null;
  cerrado_en: string | null;
  renglones_total: number;
  renglones_pendientes: number;
}

export interface ListaBandeja {
  pagina: number;
  tamano: number;
  total: number;
  saneamientos: FilaBandeja[];
}

/** Evento del historial completo de FLIT: con quién lo hizo. */
export interface EventoHistorialBackoffice extends EventoHistorial {
  usuario_id: string;
}

/** Saneamiento que ve FLIT (SP-15): mismo cuerpo que el de la compañía, con el historial completo y su usuario. */
export interface SaneamientoBackoffice {
  id: string;
  /** xmin del saneamiento (C6): se devuelve en observación, cierre, reapertura y anulación. */
  version: number;
  estado: EstadoSaneamiento;
  solicitado_en: string;
  tomado_en: string | null;
  cerrado_en: string | null;
  motivo_cierre: string | null;
  motivo_anulacion: string | null;
  observacion_flit: string | null;
  renglones: RenglonSaneamiento[];
  historial: EventoHistorialBackoffice[];
  // D-60: contexto del encabezado de PS-5 (plan §7.6: empresa, diagnóstico enlazado, placa, estado y veredicto). §6.3 no lo
  // trae; BE-B debe agregarlo al detalle del backoffice. Opcional: sin él, la placa y el veredicto salen de la deuda y el
  // diagnóstico no se enlaza.
  diagnostico_id?: string;
  diagnostico_nombre?: string;
  vehiculo_id?: string;
  placa?: string | null;
  veredicto?: "VIABLE" | "BLOQUEADO" | null;
}

export interface ItemTomar {
  empresa: string;
  id: string;
}

export interface ResultadoTomar {
  tomados: number;
  omitidos: Array<{ id: string; motivo: "TRANSICION_INVALIDA" | "NO_ENCONTRADO" }>;
}

export interface FiltrosBandeja {
  /** Vacío: todos los estados (todos los chips apagados). */
  estados: EstadoSaneamiento[];
  /** "" o el uuid de una compañía. */
  empresa: string;
  placa: string;
  /** AAAA-MM-DD sobre `solicitado_en`, o "". */
  desde: string;
  hasta: string;
  pagina: number;
  tamano: number;
}

export interface CambiosRenglon {
  resultado: ResultadoRenglon;
  nota: string | null;
  version: number;
}

export type CierreSaneamiento = { resultado: "SANEADO"; version: number } | { resultado: "NO_PROCEDE"; motivo: string; version: number };

// ---------- Constantes ----------

const ESTADOS: readonly EstadoSaneamiento[] = ["SOLICITADO", "EN_GESTION", "SANEADO", "NO_PROCEDE", "ANULADO"];
/** PS-4: por defecto, las solicitudes sin tomar. */
export const ESTADOS_BANDEJA_POR_DEFECTO: readonly EstadoSaneamiento[] = ["SOLICITADO"];
/** UA-2: el estándar de paginación del kit (HU #13916), opciones 10/20/30/50 y 10 por defecto. El servidor admite 1 a 100. */
export const TAMANO_BANDEJA_POR_DEFECTO = TAMANO_DE_PAGINA_POR_DEFECTO;
export const TAMANOS_BANDEJA: readonly number[] = TAMANOS_DE_PAGINA;
/** «Tomar» varios: 1 a 200 (§6.2). */
export const MAXIMO_TOMAR = 200;
/** Evidencia: 10 MB por defecto (§6.4, `Diagnostico:Saneamiento:EvidenciaMaximoBytes`). El servidor manda. */
export const MAXIMO_EVIDENCIA_BYTES = 10 * 1024 * 1024;

const BASE = "/backoffice/saneamientos";
const FECHA = /^\d{4}-\d{2}-\d{2}$/;
const seg = encodeURIComponent;

// ---------- Grupo A: bandeja entre compañías ----------

function entero(valor: string | null): number | null {
  if (valor === null || !/^\d+$/.test(valor)) return null;
  return Number.parseInt(valor, 10);
}

const mismosEstados = (a: readonly EstadoSaneamiento[], b: readonly EstadoSaneamiento[]) => a.length === b.length && a.every((e, i) => e === b[i]);

/**
 * Filtros de la URL de la bandeja. Sin `estado` en la URL: el de por defecto (SOLICITADO). `estado=` vacío: todos los
 * chips apagados, que equivale a todos. Lo que la API no admite se descarta.
 */
export function filtrosBandejaDesdeUrl(params: URLSearchParams): FiltrosBandeja {
  const estados = params.has("estado")
    ? ESTADOS.filter((e) => params.getAll("estado").includes(e))
    : [...ESTADOS_BANDEJA_POR_DEFECTO];
  const empresa = (params.get("empresa") ?? "").trim();
  const desde = params.get("desde") ?? "";
  const hasta = params.get("hasta") ?? "";
  const pagina = entero(params.get("pagina"));
  const tamano = entero(params.get("tamano"));
  return {
    estados,
    empresa: esEmpresaValida(empresa) ? empresa : "",
    placa: params.get("placa") ?? "",
    desde: FECHA.test(desde) ? desde : "",
    hasta: FECHA.test(hasta) ? hasta : "",
    pagina: pagina !== null && pagina >= 1 ? pagina : 1,
    tamano: tamano !== null && TAMANOS_BANDEJA.includes(tamano) ? tamano : TAMANO_BANDEJA_POR_DEFECTO,
  };
}

function parametrosBandeja(f: FiltrosBandeja, normalizar: boolean): URLSearchParams {
  const q = new URLSearchParams();
  for (const e of f.estados) q.append("estado", e);
  if (f.empresa) q.set("empresa", f.empresa);
  const placa = f.placa.trim();
  if (placa) q.set("placa", normalizar ? placa.toUpperCase() : placa);
  if (f.desde) q.set("desde", f.desde);
  if (f.hasta) q.set("hasta", f.hasta);
  return q;
}

/** Consulta de GET /backoffice/saneamientos: estados repetidos (ninguno = todos), placa en mayúsculas, página y tamaño. */
export function consultaBandeja(f: FiltrosBandeja): string {
  const q = parametrosBandeja(f, true);
  q.set("pagina", String(f.pagina));
  q.set("tamano", String(f.tamano));
  return q.toString();
}

/** URL de la bandeja con sus filtros: omite el estado por defecto, la página 1 y el tamaño por defecto. */
export function urlBandeja(f: FiltrosBandeja): string {
  const q = new URLSearchParams();
  if (f.estados.length === 0) q.set("estado", "");
  else if (!mismosEstados(f.estados, ESTADOS_BANDEJA_POR_DEFECTO)) for (const e of f.estados) q.append("estado", e);
  for (const [clave, valor] of parametrosBandeja({ ...f, estados: [] }, false)) q.append(clave, valor);
  if (f.pagina > 1) q.set("pagina", String(f.pagina));
  if (f.tamano !== TAMANO_BANDEJA_POR_DEFECTO) q.set("tamano", String(f.tamano));
  const texto = q.toString();
  return texto ? `${BASE}?${texto}` : BASE;
}

/** Algún filtro distinto del de por defecto (página y tamaño no cuentan). */
export function hayFiltrosBandeja(f: FiltrosBandeja): boolean {
  return !mismosEstados(f.estados, ESTADOS_BANDEJA_POR_DEFECTO) || Boolean(f.empresa || f.placa.trim() || f.desde || f.hasta);
}

/** GET /backoffice/saneamientos: bandeja, solicitado_en ascendente, con total. */
export async function listarBandeja(f: FiltrosBandeja, signal?: AbortSignal): Promise<ListaBandeja> {
  const respuesta = await apiFetch<ListaBandeja | null>(`${BASE}?${consultaBandeja(f)}`, { signal, cache: "no-store" });
  if (!respuesta || !Array.isArray(respuesta.saneamientos)) throw new Error("Diagnóstico respondió algo inesperado. Intenta de nuevo en un momento.");
  return respuesta;
}

/** GET /backoffice/saneamientos/resumen: solicitudes sin tomar, para la insignia del dock. */
export function obtenerResumenSaneamientos(signal?: AbortSignal): Promise<{ solicitados: number }> {
  return apiFetch<{ solicitados: number }>(`${BASE}/resumen`, { signal, cache: "no-store" });
}

/** URL del BFF del Excel de la bandeja con los mismos filtros (sin paginar). 422 DEMASIADAS_FILAS sobre 5000. */
export function urlExcelBandeja(f: FiltrosBandeja): string {
  const q = parametrosBandeja(f, true).toString();
  return `${API_PREFIX}${BASE}/excel${q ? `?${q}` : ""}`;
}

/** POST /backoffice/saneamientos/tomar: 1 a 200 items {empresa, id}; fuera de rango, 400 LISTA_INVALIDA sin salir a la red. */
export async function tomarSaneamientos(items: readonly ItemTomar[]): Promise<ResultadoTomar> {
  if (items.length === 0 || items.length > MAXIMO_TOMAR) throw new ApiError(400, `Elige entre 1 y ${MAXIMO_TOMAR} saneamientos.`, "LISTA_INVALIDA");
  return apiFetch<ResultadoTomar>(`${BASE}/tomar`, { method: "POST", body: { items: items.map(({ empresa, id }) => ({ empresa, id })) } });
}

// ---------- Grupo B: un saneamiento, con ?empresa= ----------

/** `?empresa=` o EmpresaNoElegida, antes de cualquier petición. */
function conEmpresa(empresa: string): string {
  const id = empresa.trim();
  if (!esEmpresaValida(id)) throw new EmpresaNoElegida();
  return new URLSearchParams({ empresa: id }).toString();
}

const ruta = (sid: string, sufijo: string, empresa: string) => `${BASE}/${seg(sid)}${sufijo}?${conEmpresa(empresa)}`;
const rutaRenglon = (sid: string, rid: string, sufijo: string, empresa: string) => ruta(sid, `/renglones/${seg(rid)}${sufijo}`, empresa);

/** Las llamadas del grupo B rechazan (no lanzan en el acto) si falta la compañía, como lib/backoffice/api.ts. */
async function llamar<T>(armar: () => string, opciones: RequestOptions = {}): Promise<T> {
  return apiFetch<T>(armar(), opciones);
}

const recortarONull = (texto: string | null | undefined) => (texto ?? "").trim() || null;

/** GET /backoffice/saneamientos/{sid}?empresa=: detalle con el historial completo. */
export function obtenerSaneamientoBackoffice(sid: string, empresa: string, signal?: AbortSignal): Promise<SaneamientoBackoffice> {
  return llamar(() => ruta(sid, "", empresa), { signal, cache: "no-store" });
}

/** SP-7: GET /backoffice/saneamientos/{sid}/deuda?empresa=: panel de deuda de la gestión (BE-B), auditado. */
export function obtenerDeudaBackoffice(sid: string, empresa: string, signal?: AbortSignal): Promise<PanelDeuda> {
  return llamar(() => ruta(sid, "/deuda", empresa), { signal, cache: "no-store" });
}

/** POST …/{sid}/tomar: SOLICITADO → EN_GESTION; genera los renglones. 409 TRANSICION_INVALIDA. */
export function tomarSaneamiento(sid: string, empresa: string): Promise<SaneamientoBackoffice> {
  return llamar(() => ruta(sid, "/tomar", empresa), { method: "POST" });
}

/** PUT …/{sid}/observacion: vacía se manda como null. 409 NO_EDITABLE / VERSION_DESACTUALIZADA. */
export function editarObservacion(sid: string, empresa: string, observacion: string | null, version: number): Promise<SaneamientoBackoffice> {
  return llamar(() => ruta(sid, "/observacion", empresa), { method: "PUT", body: { observacion: recortarONull(observacion), version } });
}

/** POST …/{sid}/renglones: agrega un concepto OTRO. 409 NO_EDITABLE · 422. */
export function agregarRenglon(sid: string, empresa: string, descripcion: string): Promise<RenglonSaneamiento> {
  return llamar(() => ruta(sid, "/renglones", empresa), { method: "POST", body: { descripcion: descripcion.trim() } });
}

/** PUT …/{sid}/renglones/{rid}: resultado y nota (vacía → null) con la versión del renglón. 409 VERSION_DESACTUALIZADA. */
export function actualizarRenglon(sid: string, rid: string, empresa: string, cambios: CambiosRenglon): Promise<RenglonSaneamiento> {
  return llamar(() => rutaRenglon(sid, rid, "", empresa), {
    method: "PUT",
    body: { resultado: cambios.resultado, nota: recortarONull(cambios.nota), version: cambios.version },
  });
}

/** DELETE …/{sid}/renglones/{rid}: quita el renglón (borrado lógico en el servidor, SP-2). */
export function quitarRenglon(sid: string, rid: string, empresa: string): Promise<void> {
  return llamar(() => rutaRenglon(sid, rid, "", empresa), { method: "DELETE" });
}

/**
 * PUT …/renglones/{rid}/evidencia: sube o reemplaza el PDF (multipart, campo `archivo`). Vacío (400 ARCHIVO_REQUERIDO) o
 * de más de 10 MB (413 ARCHIVO_MUY_GRANDE) no sale a la red; el tipo y la firma %PDF los valida el servidor.
 */
export async function subirEvidencia(sid: string, rid: string, empresa: string, archivo: File): Promise<RenglonSaneamiento> {
  const url = rutaRenglon(sid, rid, "/evidencia", empresa);
  if (!archivo || archivo.size === 0) throw new ApiError(400, "Elige un archivo PDF.", "ARCHIVO_REQUERIDO");
  if (archivo.size > MAXIMO_EVIDENCIA_BYTES) throw new ApiError(413, "El archivo supera 10 MB.", "ARCHIVO_MUY_GRANDE");
  const datos = new FormData();
  datos.append("archivo", archivo, archivo.name);
  return apiFetch<RenglonSaneamiento>(url, { method: "PUT", body: datos });
}

/** DELETE …/renglones/{rid}/evidencia. 409 NO_EDITABLE. */
export function quitarEvidencia(sid: string, rid: string, empresa: string): Promise<void> {
  return llamar(() => rutaRenglon(sid, rid, "/evidencia", empresa), { method: "DELETE" });
}

/** URL del BFF de la evidencia (descarga). Sin compañía válida lanza EmpresaNoElegida. */
export function urlEvidenciaBackoffice(sid: string, rid: string, empresa: string): string {
  return `${API_PREFIX}${rutaRenglon(sid, rid, "/evidencia", empresa)}`;
}

/** POST …/{sid}/cerrar: SANEADO o NO_PROCEDE (con motivo). 422 CIERRE_INCOMPLETO / MOTIVO_REQUERIDO. */
export function cerrarSaneamiento(sid: string, empresa: string, cierre: CierreSaneamiento): Promise<SaneamientoBackoffice> {
  const body = cierre.resultado === "NO_PROCEDE" ? { resultado: cierre.resultado, motivo: cierre.motivo.trim(), version: cierre.version } : cierre;
  return llamar(() => ruta(sid, "/cerrar", empresa), { method: "POST", body });
}

/** POST …/{sid}/reabrir: SANEADO o NO_PROCEDE → EN_GESTION, con motivo. 409 TRANSICION_INVALIDA. */
export function reabrirSaneamiento(sid: string, empresa: string, motivo: string, version: number): Promise<SaneamientoBackoffice> {
  return llamar(() => ruta(sid, "/reabrir", empresa), { method: "POST", body: { motivo: motivo.trim(), version } });
}

/** POST …/{sid}/anular: SOLICITADO o EN_GESTION → ANULADO, con motivo. 409 TRANSICION_INVALIDA. */
export function anularSaneamiento(sid: string, empresa: string, motivo: string, version: number): Promise<SaneamientoBackoffice> {
  return llamar(() => ruta(sid, "/anular", empresa), { method: "POST", body: { motivo: motivo.trim(), version } });
}
