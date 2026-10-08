import { apiFetch } from "./client";
import { ApiError } from "./types";

/**
 * HU #13420 (épica #13216) — Super Admin → Plataforma → Descarga masiva: parámetros del motor de
 * lotes de consolidados (`tramites.consolidado_export_settings`). Contrato en
 * `contracts/openapi/core-api.v1.yaml` (`ParametrosMotorLote`, `ActualizarParametrosMotorLoteRequest`,
 * operationIds `AdminPlataformaConsolidadoLotesGetParametros` / `…PutParametros`). Solo SuperAdmin.
 *
 * Los rangos NO se repiten aquí: llegan en `limites` (fuente única, DDL 133) y
 * {@link validarParametrosMotor} los aplica antes del PUT.
 */

const RUTA = "/api/v1/admin/plataforma/consolidados/lotes/parametros";

/** Los 12 campos numéricos editables, en el orden del contrato. */
export const CAMPOS_PARAMETROS_MOTOR = [
  "maxItemsPerBatch",
  "maxPdfsPerPart",
  "maxMbPerPart",
  "itemSlots",
  "itemTimeoutSeconds",
  "itemLeaseSeconds",
  "maxItemAttempts",
  "retryDelaySeconds",
  "partTimeoutSeconds",
  "partLeaseSeconds",
  "maxPartAttempts",
  "retentionHours",
] as const;

export type CampoParametroMotor = (typeof CAMPOS_PARAMETROS_MOTOR)[number];

/** Rango de un campo según el DDL 133; `null` = sin límite en ese extremo. */
export interface ParametroMotorLoteLimite {
  campo: string;
  minimo: number | null;
  maximo: number | null;
  /** Solo en los leases: campo cuyo valor deben superar (p. ej. `itemTimeoutSeconds`). */
  mayorQue: string | null;
}

export type ParametrosMotorLote = Record<CampoParametroMotor, number> & {
  isActive: boolean;
  updatedAt: string | null;
  updatedBy: string | null;
  updatedByName: string | null;
  rowVersion: number;
  limites: ParametroMotorLoteLimite[];
};

export type ActualizarParametrosMotorLoteRequest = Record<CampoParametroMotor, number> & {
  isActive: boolean;
  rowVersion: number;
};

/** Etiqueta visible de cada campo (también la usan los mensajes de «mayor que»). */
export const ETIQUETAS_PARAMETROS_MOTOR: Record<CampoParametroMotor, string> = {
  maxItemsPerBatch: "Tope total de trámites por lote",
  maxPdfsPerPart: "PDF por parte",
  maxMbPerPart: "MB por parte",
  itemSlots: "Carriles",
  itemTimeoutSeconds: "Tiempo máximo por trámite",
  itemLeaseSeconds: "Lease por trámite",
  maxItemAttempts: "Reintentos por trámite",
  retryDelaySeconds: "Espera entre reintentos",
  partTimeoutSeconds: "Tiempo máximo por parte",
  partLeaseSeconds: "Lease por parte",
  maxPartAttempts: "Reintentos por parte",
  retentionHours: "Horas de retención",
};

export function getParametrosMotorLote(signal?: AbortSignal): Promise<ParametrosMotorLote> {
  return apiFetch<ParametrosMotorLote>(RUTA, { method: "GET", signal });
}

export function putParametrosMotorLote(
  body: ActualizarParametrosMotorLoteRequest,
): Promise<ParametrosMotorLote> {
  const cuerpo = {
    ...Object.fromEntries(CAMPOS_PARAMETROS_MOTOR.map((c) => [c, body[c]])),
    isActive: body.isActive,
    rowVersion: body.rowVersion,
  };
  return apiFetch<ParametrosMotorLote>(RUTA, { method: "PUT", body: cuerpo });
}

const miles = (n: number) => n.toLocaleString("es-CO");

const esCampo = (c: string): c is CampoParametroMotor =>
  (CAMPOS_PARAMETROS_MOTOR as readonly string[]).includes(c);

/** Texto de ayuda con el rango del contrato (p. ej. «Entre 1 y 32.766.»), o `null` si no hay. */
export function describirLimite(limite: ParametroMotorLoteLimite | undefined): string | null {
  if (!limite) return null;
  const { minimo, maximo, mayorQue } = limite;
  if (mayorQue && esCampo(mayorQue)) {
    return `Debe ser mayor que «${ETIQUETAS_PARAMETROS_MOTOR[mayorQue]}».`;
  }
  if (minimo !== null && maximo !== null) return `Entre ${miles(minimo)} y ${miles(maximo)}.`;
  if (minimo !== null) return `Mínimo ${miles(minimo)}.`;
  if (maximo !== null) return `Máximo ${miles(maximo)}.`;
  return null;
}

/**
 * Rango de `int` (int32) del servidor: los campos con `maximo: null` (o `minimo: null`) se validan
 * contra estos topes; si no, un valor como 3.000.000.000 llegaría al PUT y el 400 no traería `errors`
 * por campo (code review Obs2).
 */
const INT32_MAX = 2147483647;
const INT32_MIN = -2147483648;

/**
 * AC3 — valida el borrador (texto de los inputs) contra `limites`. Devuelve un mensaje por campo
 * inválido; `{}` = se puede enviar el PUT.
 */
export function validarParametrosMotor(
  valores: Record<CampoParametroMotor, string>,
  limites: ParametroMotorLoteLimite[],
): Partial<Record<CampoParametroMotor, string>> {
  const errores: Partial<Record<CampoParametroMotor, string>> = {};
  const numeros: Partial<Record<CampoParametroMotor, number>> = {};

  for (const campo of CAMPOS_PARAMETROS_MOTOR) {
    const texto = (valores[campo] ?? "").trim();
    const n = /^-?\d+$/.test(texto) ? Number(texto) : Number.NaN;
    if (!Number.isSafeInteger(n)) {
      errores[campo] = "Debe ser un número entero.";
      continue;
    }
    numeros[campo] = n;
  }

  for (const limite of limites) {
    if (!esCampo(limite.campo)) continue;
    const campo = limite.campo;
    const n = numeros[campo];
    if (n === undefined) continue;
    const { minimo, maximo, mayorQue } = limite;
    const piso = minimo ?? INT32_MIN;
    const tope = maximo ?? INT32_MAX;
    if (n < piso || n > tope) {
      errores[campo] =
        minimo !== null && maximo !== null
          ? `Debe estar entre ${miles(minimo)} y ${miles(maximo)}.`
          : n < piso
            ? `Debe ser al menos ${miles(piso)}.`
            : `No puede superar ${miles(tope)}.`;
      continue;
    }
    if (mayorQue && esCampo(mayorQue)) {
      const referencia = numeros[mayorQue];
      if (referencia !== undefined && n <= referencia) {
        errores[campo] = `Debe ser mayor que «${ETIQUETAS_PARAMETROS_MOTOR[mayorQue]}» (${miles(referencia)}).`;
      }
    }
  }
  return errores;
}

/** Cómo debe reaccionar la pantalla ante un error del GET/PUT. */
export type ErrorParametrosMotor =
  | {
      tipo: "invalido";
      mensaje: string;
      errores: Partial<Record<CampoParametroMotor, string>>;
      /** Errores que no son de un campo editable (p. ej. `body`, `rowVersion`). */
      general: string | null;
    }
  | { tipo: "conflicto"; mensaje: string }
  | { tipo: "no_encontrado"; mensaje: string }
  | { tipo: "sin_permiso"; mensaje: string }
  | { tipo: "otro"; mensaje: string };

export const MENSAJE_CONFLICTO_PARAMETROS =
  "Otro Super Admin guardó los parámetros después de que los cargaste. Recarga para traer la versión vigente: lo que escribiste se conserva.";
export const MENSAJE_PARAMETROS_NO_ENCONTRADOS =
  "No existen los parámetros del motor de descarga masiva en esta instalación. Pide a soporte que revise la configuración de la base de datos.";
export const MENSAJE_SIN_PERMISO_PARAMETROS =
  "Esta pantalla es solo para usuarios con el rol SuperAdmin activo.";
const MENSAJE_GENERICO = "No se pudo completar la solicitud. Inténtalo de nuevo.";

export function interpretarErrorParametrosMotor(err: unknown): ErrorParametrosMotor {
  if (!(err instanceof ApiError)) return { tipo: "otro", mensaje: MENSAJE_GENERICO };
  const cuerpo = (err.body && typeof err.body === "object" ? err.body : {}) as Record<string, unknown>;

  if (err.status === 400) {
    const errores: Partial<Record<CampoParametroMotor, string>> = {};
    const generales: string[] = [];
    const lista = cuerpo.errors && typeof cuerpo.errors === "object" ? (cuerpo.errors as Record<string, unknown>) : {};
    for (const [clave, mensajes] of Object.entries(lista)) {
      const texto = Array.isArray(mensajes) ? mensajes.filter((m) => typeof m === "string").join(" ") : String(mensajes);
      const campo = CAMPOS_PARAMETROS_MOTOR.find((c) => c.toLowerCase() === clave.toLowerCase());
      if (campo) errores[campo] = texto;
      else if (texto) generales.push(texto);
    }
    return {
      tipo: "invalido",
      mensaje: "Revisa los campos marcados.",
      errores,
      general: generales.length ? generales.join(" ") : null,
    };
  }
  if (err.status === 409) return { tipo: "conflicto", mensaje: MENSAJE_CONFLICTO_PARAMETROS };
  if (err.status === 404) return { tipo: "no_encontrado", mensaje: MENSAJE_PARAMETROS_NO_ENCONTRADOS };
  if (err.status === 403) return { tipo: "sin_permiso", mensaje: MENSAJE_SIN_PERMISO_PARAMETROS };
  return { tipo: "otro", mensaje: MENSAJE_GENERICO };
}
