// Carga masiva del diagnóstico (HU #13754, F02-H7). Tipos y llamadas del contrato contracts/openapi/diagnostico.v1.yaml
// (ResumenValidacion, FilaResumen, ErrorFila, EstimadoFuente, FilaCapturada y ResultadoConfirmacion), en snake_case.
// El documento del locatario llega siempre enmascarado («****» y los 4 últimos): la pantalla nunca lo tiene completo,
// salvo el que el usuario vuelve a escribir al corregir una fila, que solo vive en memoria y viaja en la petición. La
// confirmación reenvía el MISMO archivo (decisión «Reenviar el archivo»), el único que trae el documento completo.
import { ApiError, apiFetch } from "@/lib/api";
import type { Fuente } from "@/lib/configuracion";

export type TipoIdentificadorVehiculo = "PLACA_CARRO" | "PLACA_MOTO" | "PLACA_REMOLQUE" | "PLACA_MAQUINARIA" | "VIN";

export interface ErrorFila {
  campo: "Identificador_Vehiculo" | "Tipo_Documento_Locatario" | "Documento_Locatario";
  code: string;
  mensaje: string;
}

export interface FilaResumen {
  fila: number;
  identificador: string;
  identificador_norm: string;
  tipo_identificador: TipoIdentificadorVehiculo | null;
  tipo_documento_locatario: string | null;
  /** Siempre enmascarado. */
  documento_locatario: string | null;
  valida: boolean;
  filas_duplicadas: number[];
  errores: ErrorFila[];
}

export interface EstimadoFuente {
  fuente: Fuente;
  consultas: number;
  /**
   * HU #13796/#13797: true en las fuentes de persona (RUNT/SIMIT propietario o locatario) cuando el número es un mínimo
   * (uno por vehículo): la pantalla lo rotula «al menos N». La API la manda siempre; ausente se trata como false.
   */
  minimo?: boolean;
}

export interface AvisoCarga {
  code: "SIN_VALIDOS" | "LIMITE_SUPERADO";
  mensaje: string;
}

export interface ResumenValidacion {
  total_filas: number;
  validos: number;
  erroneos: number;
  duplicados: number;
  puede_continuar: boolean;
  limite_vehiculos_lote: number;
  supera_limite: boolean;
  avisos: AvisoCarga[];
  filas: FilaResumen[];
  /** Solo con estimado_consultas = ANTES_DE_LANZAR en la copia de configuración. */
  estimado_consultas?: EstimadoFuente[];
}

export interface FilaCapturada {
  fila?: number;
  identificador: string | null;
  tipo_documento_locatario: string | null;
  documento_locatario: string | null;
}

/**
 * Corrección de una fila del archivo en la confirmación (CorreccionFila). Un campo ausente conserva el valor del
 * archivo; null lo deja vacío. Nunca lleva el documento enmascarado del análisis.
 */
export interface CorreccionFila {
  fila: number;
  identificador?: string | null;
  tipo_documento_locatario?: string | null;
  documento_locatario?: string | null;
}

export interface ResultadoConfirmacion {
  diagnostico_id: string;
  confirmados: number;
  invalidos: number;
  duplicados: number;
  limite_vehiculos_lote: number;
  vehiculos: unknown[];
  estimado_consultas?: EstimadoFuente[];
}

/** Ruta del BFF de la plantilla .xlsx (GET /plantilla). */
export const URL_PLANTILLA = "/api/v1/diagnostico/plantilla";

/** Extensiones que acepta el selector de archivo. */
export const EXTENSIONES_ADMITIDAS = [".xlsx", ".csv"] as const;

/** POST /diagnosticos/{id}/carga: analiza el .xlsx o .csv (multipart, campo «archivo») sin guardar filas. */
export function analizarCarga(id: string, archivo: File, signal?: AbortSignal): Promise<ResumenValidacion> {
  const cuerpo = new FormData();
  cuerpo.append("archivo", archivo, archivo.name);
  return apiFetch<ResumenValidacion>(`/diagnosticos/${encodeURIComponent(id)}/carga`, { method: "POST", body: cuerpo, signal });
}

/** POST /diagnosticos/{id}/carga/validar-filas: revalida filas corregidas con el mismo análisis; no persiste. */
export function validarFilas(id: string, filas: FilaCapturada[], signal?: AbortSignal): Promise<ResumenValidacion> {
  return apiFetch<ResumenValidacion>(`/diagnosticos/${encodeURIComponent(id)}/carga/validar-filas`, { method: "POST", body: { filas }, signal });
}

/**
 * POST /diagnosticos/{id}/vehiculos: confirma el lote → 202 y el lote queda EN_PROCESO (las consultas corren en segundo
 * plano, HU #13727). Multipart con el MISMO archivo del análisis («archivo») y, si hay, las correcciones por fila
 * («correcciones», texto JSON); el servidor re-analiza, aplica y descarta lo inválido.
 */
export function confirmarVehiculos(id: string, archivo: File, correcciones: CorreccionFila[]): Promise<ResultadoConfirmacion> {
  const cuerpo = new FormData();
  // Sin nombre explícito: un File viaja tal cual (con nombre lo envolvería en otro File).
  cuerpo.append("archivo", archivo);
  if (correcciones.length > 0) cuerpo.append("correcciones", JSON.stringify(correcciones));
  return apiFetch<ResultadoConfirmacion>(`/diagnosticos/${encodeURIComponent(id)}/vehiculos`, { method: "POST", body: cuerpo });
}

/** Documento enmascarado del análisis («****» y los 4 últimos): no se reenvía nunca. */
export const esDocumentoEnmascarado = (valor: string | null | undefined): boolean => !!valor && valor.trimStart().startsWith("*");

/** Extensión en minúsculas con el punto («.xlsx»), o cadena vacía. */
export function extensionDe(nombre: string): string {
  const punto = nombre.lastIndexOf(".");
  return punto < 0 ? "" : nombre.slice(punto).toLowerCase();
}

/** Códigos de 403 que niegan el acceso al producto (como GET /sesion, HU #13732): llevan a la página 403. */
export const CODIGOS_ACCESO_NEGADO: ReadonlySet<string> = new Set(["SIN_EMPRESA", "PRODUCTO_NO_HABILITADO"]);

/** Código de acceso negado de un error de la API, o null si no es un 403 de sesión. */
export function accesoNegado(error: unknown): string | null {
  return error instanceof ApiError && error.status === 403 && error.code && CODIGOS_ACCESO_NEGADO.has(error.code) ? error.code : null;
}
