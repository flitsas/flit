// Diagnóstico Express (HU #13755, F02-H8). Tipos y llamadas del contrato contracts/openapi/diagnostico.v1.yaml (esquemas
// ExpressEntrada, ExpressCreado, EstimadoExpress y EstimadoFuente), en snake_case. El documento del locatario solo vive
// en memoria mientras el usuario lo escribe y viaja en la petición: nunca se guarda en el navegador ni se escribe en la
// consola. La API vuelve a validar el identificador, el documento y la cabecera, y responde 422 con el campo que incumple.
import { ApiError, apiFetch } from "@/lib/api";
import type { Fuente } from "@/lib/configuracion";
import type { Alcance, CabeceraEntrada, EstadoDiagnostico, ModoDiagnostico } from "@/lib/diagnosticos/api";
import { limpiarIdentificador, validarIdentificador, validarLongitudIdentificador } from "@/lib/validation/fieldRules";

export interface ExpressEntrada {
  nombre: string | null;
  modo: ModoDiagnostico;
  tipo_tramite_codigo: string | null;
  alcance: Alcance;
  identificador: string;
  tipo_documento_locatario: string | null;
  documento_locatario: string | null;
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

export interface VehiculoExpress {
  id: string;
  orden: number;
  identificador: string;
  identificador_norm: string;
  tipo_identificador: "PLACA_CARRO" | "PLACA_MOTO" | "PLACA_REMOLQUE" | "PLACA_MAQUINARIA" | "VIN";
  tipo_documento_locatario?: string | null;
  /** Enmascarado («****» y los 4 últimos). */
  documento_locatario?: string | null;
}

export interface ExpressCreado {
  id: string;
  nombre: string;
  modo: ModoDiagnostico;
  es_express: boolean;
  estado: EstadoDiagnostico;
  creado_en: string;
  vehiculo: VehiculoExpress;
  /** Solo con estimado_consultas = ANTES_DE_LANZAR en la copia de configuración. */
  estimado_consultas?: EstimadoFuente[];
}

export interface EstimadoExpress {
  mostrar_estimado: boolean;
  estimado_consultas?: EstimadoFuente[];
}

/** Tipos de documento del locatario que acepta la API (ExpressEntrada.tipo_documento_locatario). */
export const TIPOS_DOCUMENTO_LOCATARIO = ["CC", "CE", "NIT", "PAS", "TI"] as const;

/** Campos del vehículo que un 422 de POST /express puede señalar (ErrorRegla.campo). */
export type CampoVehiculo = "identificador" | "tipo_documento_locatario" | "documento_locatario";

/** Códigos de 403 de sesión de POST /express: llevan a la página 403, como GET /sesion (HU #13732). */
const CODIGOS_ACCESO_NEGADO: ReadonlySet<string> = new Set(["SIN_USUARIO", "SIN_EMPRESA", "PRODUCTO_NO_HABILITADO"]);

/** Código de acceso negado de un error de la API, o null si no es un 403 de sesión. */
export function accesoNegadoExpress(error: unknown): string | null {
  return error instanceof ApiError && error.status === 403 && error.code && CODIGOS_ACCESO_NEGADO.has(error.code) ? error.code : null;
}

/** Campo del vehículo de un 422, o null si el error no es de un campo del vehículo. */
export function campoVehiculo(error: unknown): CampoVehiculo | null {
  if (!(error instanceof ApiError) || error.status !== 422) return null;
  return error.campo === "identificador" || error.campo === "tipo_documento_locatario" || error.campo === "documento_locatario"
    ? error.campo
    : null;
}

/**
 * Valida en pantalla el identificador antes de abrir el panel, con la regla compartida de Trámites (fieldRules) y el
 * rechazo por longitud de Diagnóstico (decisión del usuario 2026-10-21). Devuelve el mensaje de error o null.
 */
export function errorIdentificador(valor: string): string | null {
  const limpio = limpiarIdentificador(valor);
  if (!limpio) return "Escribe la placa o el VIN del vehículo.";
  const largo = validarLongitudIdentificador(limpio);
  if (largo) return largo.mensaje;
  if (validarIdentificador(limpio).tipo === null) return "No es una placa ni un VIN válido. Revisa el identificador.";
  return null;
}

/** Cuerpo de POST /express y POST /express/estimado: la cabecera de la parametrización compacta y el vehículo. */
export function entradaExpress(
  cabecera: CabeceraEntrada,
  identificador: string,
  locatario: { tipo: string; documento: string },
): ExpressEntrada {
  const documento = locatario.documento.trim();
  return {
    ...cabecera,
    identificador,
    tipo_documento_locatario: documento ? locatario.tipo || null : null,
    documento_locatario: documento || null,
  };
}

export function crearExpress(entrada: ExpressEntrada): Promise<ExpressCreado> {
  return apiFetch<ExpressCreado>("/express", { method: "POST", body: entrada });
}

export function estimarExpress(entrada: ExpressEntrada): Promise<EstimadoExpress> {
  return apiFetch<EstimadoExpress>("/express/estimado", { method: "POST", body: entrada });
}
