// Configuración de Diagnóstico en dos niveles (HU #13744, pantalla 10). Tipos y llamadas del contrato
// contracts/openapi/diagnostico.v1.yaml (esquemas Configuracion, ContratoBackoffice, EdicionOperativa y EdicionContrato):
// el JSON viaja en snake_case y las fuentes en mayúsculas. La API vuelve a validar cada permiso y cada valor.
import { apiFetch } from "@/lib/api";

export type Fuente =
  | "RUNT"
  | "RUNT_PROPIETARIO"
  | "RUNT_LOCATARIO"
  | "SIMIT_VEHICULO"
  | "SIMIT_PROPIETARIO"
  | "SIMIT_LOCATARIO"
  | "AVALUO"
  | "IMPUESTO_DEPARTAMENTAL";
export type VisibilidadUsuario = "PROPIOS" | "EMPRESA";
export type EstimadoConsultas = "NO_MOSTRAR" | "ANTES_DE_LANZAR" | "AL_FINALIZAR";

export interface NivelContrato {
  solo_lectura: boolean;
  fuentes_habilitadas: Fuente[];
  limite_vehiculos_lote: number;
  actualizado_en: string | null;
}

export interface NivelOperacion {
  visibilidad_usuario: VisibilidadUsuario;
  fuentes_por_defecto: Fuente[];
  estimado_consultas: EstimadoConsultas;
  actualizado_por: string | null;
  actualizado_en: string | null;
}

export interface Configuracion {
  empresa: string;
  contrato: NivelContrato;
  operacion: NivelOperacion;
}

export interface ContratoBackoffice {
  empresa: string;
  fuentes_habilitadas: Fuente[];
  limite_vehiculos_lote: number;
  redaccion_ia_suspendida: boolean;
  fuentes_por_defecto: Fuente[];
  actualizado_por: string | null;
  actualizado_en: string | null;
}

export type EdicionOperativa = Partial<Pick<NivelOperacion, "visibilidad_usuario" | "fuentes_por_defecto" | "estimado_consultas">>;
export type EdicionContrato = Partial<Pick<ContratoBackoffice, "fuentes_habilitadas" | "limite_vehiculos_lote" | "redaccion_ia_suspendida">>;

/** Fuentes en el orden del enum del contrato, con su nombre visible. */
export const FUENTES: ReadonlyArray<{ codigo: Fuente; nombre: string }> = [
  { codigo: "RUNT", nombre: "RUNT vehículo" },
  { codigo: "RUNT_PROPIETARIO", nombre: "RUNT propietario" },
  { codigo: "RUNT_LOCATARIO", nombre: "RUNT locatario" },
  { codigo: "SIMIT_VEHICULO", nombre: "SIMIT vehículo" },
  { codigo: "SIMIT_PROPIETARIO", nombre: "SIMIT propietario" },
  { codigo: "SIMIT_LOCATARIO", nombre: "SIMIT locatario" },
  { codigo: "AVALUO", nombre: "Avalúo comercial" },
  { codigo: "IMPUESTO_DEPARTAMENTAL", nombre: "Impuesto departamental" },
];

export const VISIBILIDADES: ReadonlyArray<{ valor: VisibilidadUsuario; nombre: string }> = [
  { valor: "EMPRESA", nombre: "Todos los diagnósticos de la compañía" },
  { valor: "PROPIOS", nombre: "Solo los propios" },
];

export const ESTIMADOS: ReadonlyArray<{ valor: EstimadoConsultas; nombre: string }> = [
  { valor: "NO_MOSTRAR", nombre: "No mostrar" },
  { valor: "ANTES_DE_LANZAR", nombre: "Antes de lanzar el diagnóstico" },
  { valor: "AL_FINALIZAR", nombre: "Al finalizar el diagnóstico" },
];

export function nombreFuente(codigo: Fuente): string {
  return FUENTES.find((f) => f.codigo === codigo)?.nombre ?? codigo;
}

/** Ordena una lista de fuentes según el enum del contrato (la API no garantiza orden). */
export function ordenarFuentes(fuentes: readonly Fuente[]): Fuente[] {
  return FUENTES.map((f) => f.codigo).filter((c) => fuentes.includes(c));
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** La ruta del backoffice exige un uuid distinto de cero (si no, la API responde 400 EMPRESA_REQUERIDA). */
export function esEmpresaValida(valor: string): boolean {
  return UUID.test(valor) && !/^[0-]+$/.test(valor);
}

/**
 * El contrato cambió después del último guardado del nivel operativo: al reducir las habilitadas, el backoffice recorta
 * las fuentes por defecto en el mismo comando, así que la compañía debe revisarlas.
 */
export function contratoCambioDespuesDeOperacion(configuracion: Configuracion): boolean {
  const contrato = configuracion.contrato.actualizado_en;
  if (!contrato) return false;
  const operacion = configuracion.operacion.actualizado_en;
  return !operacion || Date.parse(contrato) > Date.parse(operacion);
}

export function obtenerConfiguracion(signal?: AbortSignal): Promise<Configuracion> {
  return apiFetch<Configuracion>("/configuracion", { signal });
}

export function guardarOperacion(edicion: EdicionOperativa): Promise<Configuracion> {
  return apiFetch<Configuracion>("/configuracion", { method: "PUT", body: edicion });
}

function rutaContrato(empresa: string): string {
  return `/backoffice/empresas/${encodeURIComponent(empresa)}/configuracion/contrato`;
}

export function obtenerContratoEmpresa(empresa: string, signal?: AbortSignal): Promise<ContratoBackoffice> {
  return apiFetch<ContratoBackoffice>(rutaContrato(empresa), { signal });
}

export function guardarContratoEmpresa(empresa: string, edicion: EdicionContrato): Promise<ContratoBackoffice> {
  return apiFetch<ContratoBackoffice>(rutaContrato(empresa), { method: "PUT", body: edicion });
}
