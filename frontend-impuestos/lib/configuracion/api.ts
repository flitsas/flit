// Configuración de Impuestos en dos niveles (D-CONFIG; contracts/openapi/impuestos.v1.yaml 1.0.0, tag Configuracion):
// contractual (solo Super Admin, por PUT /backoffice/empresas/{id}/configuracion-contractual) y operativa (Administrador
// Impuestos, PUT /configuracion), con el máximo operativo ≤ contractual. Literales: x-flit-literales / x-flit-etiquetas.
import { apiFetch } from "@/lib/api";
import { RUTAS_BACKOFFICE } from "@/lib/backoffice/api";
import { ERRORES_EMPRESA } from "@/lib/errores";

export interface Departamento {
  codigo: string;
  nombre: string;
  activa?: boolean;
}

export interface ConfiguracionImpuestos {
  contractual: { maxRegistrosLote: number; departamentosHabilitados: string[] | null };
  operativa: { maxRegistrosLote: number | null; correosAviso: string[] };
  departamentosDisponibles: Departamento[];
}

export interface ConfiguracionOperativaEntrada {
  maxRegistrosLote: number | null;
  correosAviso: string[];
}

export interface ConfiguracionContractualEntrada {
  maxRegistrosLote: number;
  departamentosHabilitados: string[] | null;
}

/** Máximo absoluto del contrato (schema: maximum 5000). */
export const MAXIMO_ABSOLUTO_LOTE = 5000;

/** x-flit-literales.errores de las rutas de configuración (`{tope}` lo sustituye mensajeErrorFijo). */
export const ERRORES_CONFIGURACION: Readonly<Record<string, string>> = {
  TOPE_SUPERA_CONTRACTUAL: "El máximo por lote no puede superar el tope contractual de {tope}.",
  TOPE_INVALIDO: "El máximo por lote debe ser un número entre 1 y 5000.",
  CORREO_INVALIDO: "Un correo no es válido.",
  DEPARTAMENTO_NO_DISPONIBLE: "Un departamento no está en la lista del proveedor.",
  ...ERRORES_EMPRESA,
  PRODUCTO_SUSPENDIDO: "Impuestos está suspendido para la compañía: solo lectura.",
};

const RUTA = "/configuracion";

export const obtenerConfiguracion = (empresa: string | null, signal?: AbortSignal) => apiFetch<ConfiguracionImpuestos>(RUTA, { signal, empresa });

/**
 * PUT /configuracion. El contrato 1.0.0 no declara `empresa` en el PUT (sí en el GET); se propaga igual para que el Super
 * Admin guarde la operativa de la compañía elegida, y queda reportado como hueco de contrato.
 */
export const guardarConfiguracionOperativa = (entrada: ConfiguracionOperativaEntrada, empresa: string | null) =>
  apiFetch<ConfiguracionImpuestos>(RUTA, { method: "PUT", body: entrada, empresa });

export const guardarConfiguracionContractual = (tenantId: string, entrada: ConfiguracionContractualEntrada) =>
  apiFetch<ConfiguracionImpuestos>(RUTAS_BACKOFFICE.configuracionContractual(tenantId), { method: "PUT", body: entrada });
