// Mensajes de los errores de la API de administración del portal de Persona Natural (HU #14071 a #14073).
// El backend responde `{ error, message }` con un texto en español pensado para pantalla; aquí se sobreescriben solo
// los códigos que merecen una explicación más útil y el resto usa el `message` del servidor.
import { ApiError } from "@/lib/api/types";

const OVERRIDES: Record<string, string> = {
  tarifa_solapada:
    "Ya existe una tarifa para ese concepto, tipo y organismo que empieza ese mismo día o después. Elija una fecha posterior a la versión vigente.",
  fecha_inicio_pasada: "La fecha de inicio no puede ser anterior a hoy.",
  tipo_no_elegible:
    "Ese tipo de trámite no es elegible para Persona Natural: necesita el asistente encendido y publicado en FLIT 2.",
  conflicto_de_version:
    "Otra persona cambió esta configuración mientras usted editaba. Recargue los valores actuales para continuar.",
};

export interface PnErrorInfo {
  status: number | null;
  code: string | null;
  message: string;
  /** Cuerpo completo (p. ej. `actual` en el 409 de parámetros). */
  body: Record<string, unknown> | null;
}

export function pnError(err: unknown, fallback = "No se pudo completar la operación. Inténtelo de nuevo."): PnErrorInfo {
  if (err instanceof ApiError) {
    const body = (err.body && typeof err.body === "object" ? (err.body as Record<string, unknown>) : null) ?? null;
    const code = typeof body?.error === "string" ? body.error : null;
    if (code && OVERRIDES[code]) return { status: err.status, code, message: OVERRIDES[code], body };
    if (err.status === 403) {
      return {
        status: 403,
        code,
        message: "Solo el superadministrador con el permiso de Persona Natural puede hacer este cambio.",
        body,
      };
    }
    const serverMessage = typeof body?.message === "string" && body.message.trim() ? body.message : null;
    return { status: err.status, code, message: serverMessage ?? fallback, body };
  }
  return { status: null, code: null, message: fallback, body: null };
}

export function pnErrorMessage(err: unknown, fallback?: string): string {
  return pnError(err, fallback).message;
}
