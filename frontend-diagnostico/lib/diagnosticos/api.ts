// Cabecera del diagnóstico (HU #13752, F01-H5). Tipos y llamadas del contrato contracts/openapi/diagnostico.v1.yaml
// (esquemas CabeceraEntrada, Alcance, Diagnostico, TiposTramite y ErrorRegla): el JSON viaja en snake_case, salvo
// `fuentesObligatorias` del catálogo de tipos de trámite, que el contrato publica en camelCase. La API vuelve a validar
// cada regla (nombre, grupos, RUNT, fuentes habilitadas y tipo de trámite) y responde 422 con el campo que incumple.
import { apiFetch } from "@/lib/api";
import { ordenarFuentes, type EstimadoConsultas, type Fuente, type VisibilidadUsuario } from "@/lib/configuracion";

export type ModoDiagnostico = "SIMPLE" | "CON_ANALISIS";
export type EstadoDiagnostico = "CREADO" | "EN_PROCESO" | "COMPLETADO" | "INCOMPLETO";
export type GrupoAlcance = "por_vehiculo" | "por_propietario" | "por_locatario";

export interface Alcance {
  por_vehiculo: Fuente[];
  por_propietario: Fuente[];
  por_locatario: Fuente[];
}

export interface CabeceraEntrada {
  nombre: string;
  modo: ModoDiagnostico;
  tipo_tramite_codigo: string | null;
  alcance: Alcance;
}

export interface Diagnostico {
  id: string;
  nombre: string;
  modo: ModoDiagnostico;
  tipo_tramite_codigo: string | null;
  es_express: boolean;
  estado: EstadoDiagnostico;
  alcance: Alcance;
  configuracion: {
    fuentes_habilitadas: Fuente[];
    limite_vehiculos_lote: number;
    visibilidad_usuario: VisibilidadUsuario;
    fuentes_por_defecto: Fuente[];
    estimado_consultas: EstimadoConsultas;
  };
  usuario_creador_id: string;
  creado_en: string;
}

export interface TipoTramite {
  codigo: string;
  nombre: string;
  familia: string;
  /** Fuentes que el tipo exige en el alcance (R-17). Llega vacía en D1. */
  fuentesObligatorias: string[];
}

/** Máximo del nombre según CabeceraEntrada (maxLength 150). */
export const NOMBRE_MAXIMO = 150;

/** Fuentes de cada grupo del alcance (R-24), en el orden del enum del contrato. */
export const GRUPOS: ReadonlyArray<{ grupo: GrupoAlcance; nombre: string; fuentes: readonly Fuente[] }> = [
  { grupo: "por_vehiculo", nombre: "Por Vehículo", fuentes: ["RUNT", "SIMIT_VEHICULO", "AVALUO", "IMPUESTO_DEPARTAMENTAL"] },
  { grupo: "por_propietario", nombre: "Por Propietario", fuentes: ["RUNT_PROPIETARIO", "SIMIT_PROPIETARIO"] },
  { grupo: "por_locatario", nombre: "Por Locatario", fuentes: ["RUNT_LOCATARIO", "SIMIT_LOCATARIO"] },
];

/** Fuente que va siempre en el alcance (la API responde 422 ALCANCE_SIN_RUNT sin ella). */
export const FUENTE_SIEMPRE: Fuente = "RUNT";

/**
 * Fuentes que el backend rechaza aunque el contrato las habilite (422 FUENTE_NO_DISPONIBLE): IMPUESTO_DEPARTAMENTAL no
 * tiene adaptador mientras Diagnostico:Fuentes:ImpuestoDisponible sea false. No se ofrecen.
 */
export const FUENTES_NO_DISPONIBLES: readonly Fuente[] = ["IMPUESTO_DEPARTAMENTAL"];

/** Fuentes que se pueden ofrecer: las habilitadas del contrato sin las no disponibles, y RUNT siempre. */
export function fuentesOfrecidas(habilitadas: readonly Fuente[]): Fuente[] {
  return ordenarFuentes([FUENTE_SIEMPRE, ...habilitadas.filter((f) => !FUENTES_NO_DISPONIBLES.includes(f) && f !== FUENTE_SIEMPRE)]);
}

/** Reparte una lista de fuentes en los tres grupos del alcance, en el orden del enum y sin repetir. */
export function armarAlcance(fuentes: readonly Fuente[]): Alcance {
  const ordenadas = ordenarFuentes(fuentes);
  const del = (grupo: GrupoAlcance) => ordenadas.filter((f) => GRUPOS.find((g) => g.grupo === grupo)?.fuentes.includes(f));
  return { por_vehiculo: del("por_vehiculo"), por_propietario: del("por_propietario"), por_locatario: del("por_locatario") };
}

export async function listarTiposTramite(signal?: AbortSignal): Promise<TipoTramite[]> {
  const respuesta = await apiFetch<{ tipos: TipoTramite[] }>("/tipos-tramite", { signal });
  return respuesta?.tipos ?? [];
}

export function crearDiagnostico(cabecera: CabeceraEntrada): Promise<Diagnostico> {
  return apiFetch<Diagnostico>("/diagnosticos", { method: "POST", body: cabecera });
}

export function editarDiagnostico(id: string, cabecera: CabeceraEntrada): Promise<Diagnostico> {
  return apiFetch<Diagnostico>(`/diagnosticos/${encodeURIComponent(id)}`, { method: "PUT", body: cabecera });
}
