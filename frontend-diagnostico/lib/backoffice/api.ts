// Backoffice FLIT de Diagnóstico (HU #13802, F06-H5). Tipos y llamadas de los contratos de HU #13799 (lotes, grilla con el
// origen de cada celda) y HU #13800 (formulario y llenado manual), en contracts/openapi/diagnostico.v1.yaml, en snake_case.
// La compañía nunca sale de la sesión: toda llamada lleva `?empresa=` explícito y, sin una compañía elegida, no se llama.
// El origen manual de una celda solo se muestra en esta pantalla (Q5); la vista de la compañía no lo conoce.
import { ApiError, apiFetch } from "@/lib/api";
import { esEmpresaValida, type Fuente } from "@/lib/configuracion";
import type { EstadoDiagnostico } from "@/lib/diagnosticos/api";
import { BLOQUEADA_POR_RUNT, type CeldaGrilla, type EstadoCelda, type FilaVehiculo, type Grilla } from "@/lib/tablero/api";
import type { ConsultaPersona } from "@/lib/tablero/detalle";

export type OrigenCelda = "API" | "MANUAL";

export interface CeldaGrillaBackoffice extends CeldaGrilla {
  /** API, MANUAL o null (sin dato). La reutilizada del mismo día es API con `reutilizada=true`. */
  origen: OrigenCelda | null;
  /** Solo con origen MANUAL. */
  usuario_manual_id: string | null;
}

export interface FilaVehiculoBackoffice extends Omit<FilaVehiculo, "celdas"> {
  celdas: CeldaGrillaBackoffice[];
}

export interface GrillaBackoffice extends Omit<Grilla, "vehiculos"> {
  empresa: string;
  vehiculos: FilaVehiculoBackoffice[];
}

export interface DiagnosticoBackoffice {
  id: string;
  nombre: string;
  estado: EstadoDiagnostico;
  creado_en: string;
  vehiculos: number;
  celdas_en_error: number;
}

export interface ListaDiagnosticosBackoffice {
  empresa: string;
  pagina: number;
  tamano: number;
  total: number;
  diagnosticos: DiagnosticoBackoffice[];
}

export interface FiltrosLotes {
  /** Sin estado: todos. */
  estado?: EstadoDiagnostico | null;
  /** true: solo lotes con alguna celda en ERROR; sin el filtro, todos. */
  conErrores?: boolean;
  pagina?: number;
  tamano?: number;
}

export type MotivoNoManual = "FUENTE_NO_ADMITE_MANUAL" | "CELDA_NO_EN_ERROR" | "CELDA_BLOQUEADA_POR_RUNT" | "SIN_PERSONAS_EN_ERROR";

export interface PersonaEnError {
  persona_id: string;
  orden: number;
  rol: "PROPIETARIO" | "LOCATARIO";
  tipo_documento: string;
  /** «****» más los cuatro últimos caracteres; se muestra tal cual llega. */
  documento_enmascarado: string;
  declarado_por_cliente: boolean;
  /** Define el esquema del resultado de la persona. */
  consulta: ConsultaPersona;
  tipo_error: string | null;
}

export interface FormularioManual {
  id: string;
  diagnostico_id: string;
  vehiculo_id: string;
  fuente: Fuente;
  estado: EstadoCelda;
  tipo_error: string | null;
  /** Versión de la celda: va en el PUT. */
  version: number;
  admite_manual: boolean;
  motivo: MotivoNoManual | null;
  /** null en las fuentes del vehículo; en las de persona, SOLO las personas en ERROR, por orden. */
  personas: PersonaEnError[] | null;
}

export type CuerpoLlenado =
  | { version: number; resultado: Record<string, unknown> }
  | { version: number; personas: { persona_id: string; resultado: Record<string, unknown> }[] };

export interface RespuestaLlenado {
  id: string;
  diagnostico_id: string;
  vehiculo_id: string;
  fuente: Fuente;
  estado: "COMPLETADO";
  origen: "MANUAL";
  usuario_manual_id: string;
  dato_obtenido_en: string;
  /** Nueva versión del tablero. */
  version: number;
  estado_lote: EstadoDiagnostico;
}

/** Se pidió una llamada del backoffice sin una compañía válida: no sale ninguna petición. */
export class EmpresaNoElegida extends Error {
  constructor() {
    super("Elige una compañía antes de consultar el backoffice.");
    this.name = "EmpresaNoElegida";
  }
}

/** `?empresa=` (y el resto de la consulta) o EmpresaNoElegida, antes de cualquier petición. */
function consulta(empresa: string, extra: Record<string, string | number | undefined> = {}): string {
  const id = empresa.trim();
  if (!esEmpresaValida(id)) throw new EmpresaNoElegida();
  const params = new URLSearchParams({ empresa: id });
  for (const [clave, valor] of Object.entries(extra)) if (valor !== undefined) params.set(clave, String(valor));
  return params.toString();
}

const BASE = "/backoffice";
const seg = encodeURIComponent;

/** GET /backoffice/diagnosticos?empresa=&estado=&con_errores=&pagina=&tamano= (orden creado_en desc). */
export async function listarLotesBackoffice(empresa: string, filtros: FiltrosLotes = {}, signal?: AbortSignal): Promise<ListaDiagnosticosBackoffice> {
  const q = consulta(empresa, {
    estado: filtros.estado ?? undefined,
    con_errores: filtros.conErrores ? "true" : undefined,
    pagina: filtros.pagina ?? 1,
    tamano: filtros.tamano,
  });
  return apiFetch<ListaDiagnosticosBackoffice>(`${BASE}/diagnosticos?${q}`, { signal });
}

/** GET /backoffice/diagnosticos/{id}/grilla?empresa=&desde=: misma semántica incremental que la grilla de la compañía. */
export async function obtenerGrillaBackoffice(id: string, empresa: string, desde: number, signal?: AbortSignal): Promise<GrillaBackoffice> {
  const q = consulta(empresa, { desde });
  return apiFetch<GrillaBackoffice>(`${BASE}/diagnosticos/${seg(id)}/grilla?${q}`, { signal });
}

/** GET /backoffice/consultas/{cid}/manual?empresa=: versión, si admite el llenado y las personas en error. */
export async function obtenerFormularioManual(consultaId: string, empresa: string, signal?: AbortSignal): Promise<FormularioManual> {
  const q = consulta(empresa);
  return apiFetch<FormularioManual>(`${BASE}/consultas/${seg(consultaId)}/manual?${q}`, { signal });
}

/** PUT /backoffice/consultas/{cid}/manual?empresa=: la celda pasa a COMPLETADO con origen MANUAL. */
export async function llenarConsultaManual(consultaId: string, empresa: string, cuerpo: CuerpoLlenado): Promise<RespuestaLlenado> {
  const q = consulta(empresa);
  return apiFetch<RespuestaLlenado>(`${BASE}/consultas/${seg(consultaId)}/manual?${q}`, { method: "PUT", body: cuerpo });
}

/** Fuentes que el backoffice puede llenar a mano: todas menos RUNT (Q3) e impuesto departamental (R-9). */
export const FUENTES_LLENABLES: ReadonlySet<Fuente> = new Set<Fuente>([
  "SIMIT_VEHICULO",
  "AVALUO",
  "RUNT_PROPIETARIO",
  "RUNT_LOCATARIO",
  "SIMIT_PROPIETARIO",
  "SIMIT_LOCATARIO",
]);

/** «Llenar a mano» se ofrece en una celda en ERROR, no bloqueada por el RUNT, de una fuente llenable. La API lo revalida. */
export function esLlenable(celda: CeldaGrilla): boolean {
  return celda.estado === "ERROR" && celda.tipo_error !== BLOQUEADA_POR_RUNT && FUENTES_LLENABLES.has(celda.fuente);
}

export interface ErrorLlenado {
  /** CAMPO: va junto al campo `campo`; AVISO: aviso general del formulario. */
  tipo: "CAMPO" | "AVISO";
  campo?: string;
  mensaje: string;
  /** La celda cambió (409): hay que volver a leer la grilla. */
  recargarGrilla: boolean;
}

const AVISOS_409: Record<string, string> = {
  CELDA_MODIFICADA: "La celda cambió mientras la llenabas (por ejemplo, por un reproceso). La grilla se vuelve a leer; ábrela de nuevo si sigue en error.",
  CELDA_NO_EN_ERROR: "Esta celda ya no está en error: no se puede llenar a mano.",
  CELDA_BLOQUEADA_POR_RUNT: "Esta celda depende del RUNT del vehículo, que también falló: no se llena a mano.",
};

/** 422 con campo → junto al campo; 409, 403, 404 y el resto → aviso del formulario. Nada cambia en ningún error. */
export function traducirErrorLlenado(error: unknown): ErrorLlenado {
  if (error instanceof ApiError) {
    if (error.status === 422 && error.campo && (error.code === "RESULTADO_INVALIDO" || error.code === "PERSONA_NO_EN_ERROR"))
      return { tipo: "CAMPO", campo: error.campo, mensaje: error.message, recargarGrilla: false };
    if (error.status === 409)
      return { tipo: "AVISO", mensaje: (error.code && AVISOS_409[error.code]) || error.message, recargarGrilla: true };
    if (error.status === 422 && error.code === "FUENTE_NO_ADMITE_MANUAL")
      return { tipo: "AVISO", mensaje: "Esta fuente no se llena a mano.", recargarGrilla: false };
    if (error.status === 403) return { tipo: "AVISO", mensaje: "No tienes permiso para llenar consultas a mano.", recargarGrilla: false };
    if (error.status === 404) return { tipo: "AVISO", mensaje: "La consulta no existe en esta compañía.", recargarGrilla: true };
  }
  if (error instanceof EmpresaNoElegida) return { tipo: "AVISO", mensaje: error.message, recargarGrilla: false };
  return { tipo: "AVISO", mensaje: error instanceof Error ? error.message : "No se pudo llenar la consulta.", recargarGrilla: false };
}

const RESUELTA = (c: CeldaGrilla) => c.estado === "COMPLETADO" || c.estado === "NA";

/** Pone la respuesta del PUT en la grilla en memoria: la celda queda verde con origen MANUAL y el lote con su nuevo estado. */
export function aplicarLlenado(grilla: GrillaBackoffice, respuesta: RespuestaLlenado): GrillaBackoffice {
  const vehiculos = grilla.vehiculos.map((v) => {
    if (!v.celdas.some((c) => c.id === respuesta.id)) return v;
    const celdas = v.celdas.map((c) =>
      c.id === respuesta.id
        ? {
            ...c,
            estado: respuesta.estado,
            tipo_error: null,
            motivo_na: null,
            reutilizada: false,
            dato_obtenido_en: respuesta.dato_obtenido_en,
            version: respuesta.version,
            origen: respuesta.origen,
            usuario_manual_id: respuesta.usuario_manual_id,
          }
        : c,
    );
    return { ...v, celdas, terminado: celdas.every(RESUELTA) };
  });
  return {
    ...grilla,
    estado: respuesta.estado_lote,
    vehiculos,
    avance: { ...grilla.avance, terminados: vehiculos.filter((v) => v.terminado).length },
  };
}
