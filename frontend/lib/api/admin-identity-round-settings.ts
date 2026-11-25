import { apiFetch } from "./client";
import { ApiError } from "./types";

/**
 * Configuración GLOBAL de rondas de validación de identidad (SuperAdmin de FLIT).
 * GET/PUT `/api/v1/admin/identity/round-settings`. 400 con `details[]`; 403 si no es SuperAdmin.
 */

const RUTA = "/api/v1/admin/identity/round-settings";

export const RONDAS_MIN = 1;
export const RONDAS_MAX = 10;
export const RONDAS_POR_DEFECTO = 3;
export const MAX_CORREOS = 20;

export type DisparadorAlerta = "al_maximo" | "cada_ronda";

export interface IdentityRoundSettings {
  rondasMaximas: number;
  disparadorAlerta: DisparadorAlerta;
  correosAlerta: string[];
}

export type CampoRondas = "rondasMaximas" | "disparadorAlerta" | "correosAlerta";

export function getIdentityRoundSettings(signal?: AbortSignal): Promise<IdentityRoundSettings> {
  return apiFetch<IdentityRoundSettings>(RUTA, { method: "GET", signal });
}

export function putIdentityRoundSettings(body: IdentityRoundSettings): Promise<IdentityRoundSettings> {
  return apiFetch<IdentityRoundSettings>(RUTA, {
    method: "PUT",
    body: {
      rondasMaximas: body.rondasMaximas,
      disparadorAlerta: body.disparadorAlerta,
      correosAlerta: body.correosAlerta,
    },
  });
}

const EMAIL_RE = /^[^\s@,;<>()]+@[^\s@,;<>()]+\.[^\s@,;<>()]{2,}$/;

export const normalizarCorreo = (c: string) => c.trim().toLowerCase();

export function esCorreoValido(c: string): boolean {
  const t = c.trim();
  return t.length <= 254 && EMAIL_RE.test(t);
}

/** Valida el texto de «rondas máximas». Devuelve el mensaje o `null` si es válido. */
export function validarRondas(texto: string): string | null {
  const t = texto.trim();
  if (!/^\d+$/.test(t)) return `Escribe un número entero entre ${RONDAS_MIN} y ${RONDAS_MAX}.`;
  const n = Number(t);
  if (n < RONDAS_MIN || n > RONDAS_MAX) return `Debe estar entre ${RONDAS_MIN} y ${RONDAS_MAX}.`;
  return null;
}

/** Valida un correo que se quiere agregar a la lista. */
export function validarNuevoCorreo(texto: string, existentes: string[]): string | null {
  const t = texto.trim();
  if (!t) return "Escribe un correo.";
  if (!esCorreoValido(t)) return "El correo no tiene un formato válido.";
  if (existentes.some((c) => normalizarCorreo(c) === normalizarCorreo(t))) return "Ese correo ya está en la lista.";
  if (existentes.length >= MAX_CORREOS) return `Máximo ${MAX_CORREOS} correos.`;
  return null;
}

export type ErrorRondas =
  | { tipo: "invalido"; mensaje: string; errores: Partial<Record<CampoRondas, string>>; general: string | null }
  | { tipo: "sin_permiso"; mensaje: string }
  | { tipo: "otro"; mensaje: string };

export const MENSAJE_SIN_PERMISO_RONDAS = "Esta pantalla es solo para el SuperAdmin de FLIT.";
const MENSAJE_GENERICO = "No se pudo completar la solicitud. Inténtalo de nuevo.";

function campoDeTexto(texto: string): CampoRondas | null {
  const t = texto.toLowerCase();
  if (/correo|email|mail|duplicad|repetid/.test(t)) return "correosAlerta";
  if (/ronda|rango/.test(t)) return "rondasMaximas";
  if (/disparador|alerta/.test(t)) return "disparadorAlerta";
  return null;
}

function campoDeClave(clave: string): CampoRondas | null {
  const k = clave.toLowerCase();
  if (k.startsWith("correosalerta") || k.startsWith("correos")) return "correosAlerta";
  if (k.startsWith("rondasmaximas") || k.startsWith("rondas")) return "rondasMaximas";
  if (k.startsWith("disparadoralerta") || k.startsWith("disparador")) return "disparadorAlerta";
  return null;
}

/** Mapea el error del GET/PUT; los `details[]` del 400 se asignan al campo que corresponde. */
export function interpretarErrorRondas(err: unknown): ErrorRondas {
  if (!(err instanceof ApiError)) return { tipo: "otro", mensaje: MENSAJE_GENERICO };
  if (err.status === 403) return { tipo: "sin_permiso", mensaje: MENSAJE_SIN_PERMISO_RONDAS };
  if (err.status !== 400) return { tipo: "otro", mensaje: MENSAJE_GENERICO };

  const cuerpo = (err.body && typeof err.body === "object" ? err.body : {}) as Record<string, unknown>;
  const detalles = Array.isArray(cuerpo.details) ? cuerpo.details : [];
  const porCampo: Partial<Record<CampoRondas, string[]>> = {};
  const generales: string[] = [];
  for (const d of detalles) {
    let campo: CampoRondas | null = null;
    let texto = "";
    if (typeof d === "string") {
      texto = d;
      campo = campoDeTexto(d);
    } else if (d && typeof d === "object") {
      const o = d as Record<string, unknown>;
      texto = String(o.message ?? o.mensaje ?? o.error ?? "");
      const clave = String(o.field ?? o.campo ?? o.path ?? "");
      campo = (clave && campoDeClave(clave)) || campoDeTexto(texto);
    }
    if (!texto) continue;
    if (campo) (porCampo[campo] ??= []).push(texto);
    else generales.push(texto);
  }
  const errores: Partial<Record<CampoRondas, string>> = {};
  for (const [c, m] of Object.entries(porCampo)) errores[c as CampoRondas] = (m ?? []).join(" ");
  return {
    tipo: "invalido",
    mensaje: "Revisa los campos marcados.",
    errores,
    general: generales.length ? generales.join(" ") : null,
  };
}
