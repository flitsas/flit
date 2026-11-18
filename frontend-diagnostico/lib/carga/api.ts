// Carga masiva del diagnóstico (HU #13754, F02-H7). Tipos y llamadas del contrato contracts/openapi/diagnostico.v1.yaml
// (ResumenValidacion, FilaResumen, ErrorFila, EstimadoFuente, FilaCapturada y ResultadoConfirmacion), en snake_case.
// HU #14585 (contrato §1, C1/C2): el documento declarado es el del propietario actual. Llega siempre enmascarado («****» y los 4 últimos): la pantalla nunca lo tiene completo,
// salvo el que el usuario vuelve a escribir al corregir una fila, que solo vive en memoria y viaja en la petición. La
// confirmación reenvía el MISMO archivo (decisión «Reenviar el archivo»), el único que trae el documento completo.
import { ApiError, apiFetch } from "@/lib/api";
import type { Fuente } from "@/lib/configuracion";

export type TipoIdentificadorVehiculo =
  | "PLACA_CARRO"
  | "PLACA_MOTO"
  | "PLACA_REMOLQUE"
  | "PLACA_MAQUINARIA"
  | "PLACA_MOTOCARRO"
  | "VIN";

export interface ErrorFila {
  /**
   * Columna del archivo. HU #14585: cabeceras nuevas `*_Propietario`; la API acepta las viejas `*_Locatario` como alias
   * (plantillas que ya circulan), así que un error puede venir con cualquiera de las dos.
   */
  campo: "Identificador_Vehiculo" | "Tipo_Documento_Propietario" | "Documento_Propietario" | "Tipo_Documento_Locatario" | "Documento_Locatario";
  code: string;
  mensaje: string;
}

/** Columnas del documento declarado; las `*_Locatario` son el alias que la API sigue aceptando (HU #14585, C2). */
export const ERRORES_DE_DOCUMENTO: readonly ErrorFila["campo"][] = [
  "Tipo_Documento_Propietario",
  "Documento_Propietario",
  "Tipo_Documento_Locatario",
  "Documento_Locatario",
];

export interface FilaResumen {
  fila: number;
  identificador: string;
  identificador_norm: string;
  tipo_identificador: TipoIdentificadorVehiculo | null;
  tipo_documento_propietario: string | null;
  /** Siempre enmascarado. */
  documento_propietario: string | null;
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
  tipo_documento_propietario: string | null;
  documento_propietario: string | null;
}

/**
 * Corrección de una fila del archivo en la confirmación (CorreccionFila). Un campo ausente conserva el valor del
 * archivo; null lo deja vacío. Nunca lleva el documento enmascarado del análisis.
 */
export interface CorreccionFila {
  fila: number;
  identificador?: string | null;
  tipo_documento_propietario?: string | null;
  documento_propietario?: string | null;
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

/**
 * AP-9 (SF-6): texto fijo por `code` de POST …/carga, …/carga/validar-filas y …/vehiculos (contrato
 * diagnostico.v1.yaml). Equivale al motivo que da el servidor, sin sus números ni nombres: la UI nunca pinta su `message`.
 */
export const ERRORES_CARGA: Readonly<Record<string, string>> = {
  NO_ENCONTRADO: "No encontramos este diagnóstico en tu compañía.",
  DIAGNOSTICO_NO_EDITABLE: "El diagnóstico ya se lanzó: ya no admite cambios en la carga.",
  ARCHIVO_DEMASIADO_GRANDE: "El archivo supera el tamaño máximo permitido: divídelo en varios archivos y vuelve a subirlos.",
  FORMATO_NO_ADMITIDO: "Formato de archivo no admitido: sube la plantilla en .xlsx o un .csv en UTF-8 separado por coma o punto y coma.",
  ARCHIVO_ILEGIBLE:
    "No se pudo leer el archivo: si es .xlsx, ábrelo en Excel y guárdalo de nuevo como .xlsx; si es .csv, guárdalo como «CSV UTF-8». Luego vuelve a subirlo.",
  FORMATO_XLS_NO_ADMITIDO: "El formato .xls no se admite: guarda el archivo como .xlsx y vuelve a subirlo.",
  ARCHIVO_REQUERIDO: "Adjunta el archivo de carga (.xlsx o .csv).",
  COLUMNA_REQUERIDA: "El archivo no tiene la columna Identificador_Vehiculo en la primera fila: usa la plantilla.",
  SIN_VALIDOS: "No hay ningún vehículo válido para confirmar: corrige los identificadores o carga otro archivo.",
  LIMITE_SUPERADO: "Los vehículos válidos superan el límite por lote de tu contrato: divide el lote o retira vehículos.",
  CORRECCIONES_INVALIDAS: "No se pudieron aplicar las correcciones de las filas: revisa las filas corregidas y confirma de nuevo.",
  VEHICULOS_YA_CONFIRMADOS: "El diagnóstico ya tiene sus vehículos confirmados.",
  // HU #14585 (contrato §1.1): el archivo trae a la vez las columnas nuevas y las viejas del documento declarado.
  ENCABEZADO_AMBIGUO:
    "El archivo trae dos veces las columnas del documento: deja solo Tipo_Documento_Propietario y Documento_Propietario (propietario actual) y vuelve a subirlo.",
  DEMASIADAS_SOLICITUDES: "Hiciste demasiadas solicitudes seguidas. Espera un momento y vuelve a intentarlo.",
};

/** Códigos de 403 que niegan el acceso al producto (como GET /sesion, HU #13732): llevan a la página 403. */
export const CODIGOS_ACCESO_NEGADO: ReadonlySet<string> = new Set(["SIN_EMPRESA", "PRODUCTO_NO_HABILITADO"]);

/** Código de acceso negado de un error de la API, o null si no es un 403 de sesión. */
export function accesoNegado(error: unknown): string | null {
  return error instanceof ApiError && error.status === 403 && error.code && CODIGOS_ACCESO_NEGADO.has(error.code) ? error.code : null;
}
