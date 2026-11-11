// Mensajes de los errores de la API de administración del portal de Persona Natural (HU #14071 a #14073).
// El backend responde `{ error, message }` con un texto en español pensado para pantalla; aquí se sobreescriben los
// códigos que merecen una explicación en lenguaje de negocio y el resto usa el `message` del servidor. Nunca se
// muestran números de estado ni identificadores técnicos.
import { ApiError } from "@/lib/api/types";

const OVERRIDES: Record<string, string> = {
  tarifa_solapada:
    "Ya hay una tarifa para este trámite y organismo que se cruza con esa fecha. Elige otra fecha de inicio.",
  fecha_inicio_pasada: "La fecha de inicio no puede ser anterior a hoy.",
  tipo_no_elegible:
    "Este trámite todavía no está listo para ofrecerse en el portal: su asistente debe estar activo y publicado en FLIT 2.",
  conflicto_de_version:
    "Otra persona guardó cambios en estos ajustes mientras editabas. Carga los valores actuales y vuelve a aplicar tu cambio.",
};

const GENERICO = "No se pudo completar la operación. Inténtalo de nuevo.";
const SIN_CONEXION = "No hay conexión con FLIT en este momento. Revisa tu internet e inténtalo de nuevo.";
const ERROR_DE_SERVIDOR = "FLIT tuvo un problema al guardar. Inténtalo de nuevo en unos minutos.";

export interface PnErrorInfo {
  status: number | null;
  code: string | null;
  message: string;
  /** Cuerpo completo (p. ej. `actual` en el 409 de parámetros). */
  body: Record<string, unknown> | null;
}

export function pnError(err: unknown, fallback = GENERICO): PnErrorInfo {
  if (err instanceof ApiError) {
    const body = (err.body && typeof err.body === "object" ? (err.body as Record<string, unknown>) : null) ?? null;
    // Plataforma responde `{ error, message }`; el portal del OT, `{ code, message }`.
    const codigoOt = typeof body?.code === "string" ? body.code : null;
    const code = typeof body?.error === "string" ? body.error : codigoOt;
    if (code && OVERRIDES[code]) return { status: err.status, code, message: OVERRIDES[code], body };
    // En el hub del OT el 403 trae su propio motivo exacto (concepto ajeno, otro organismo): se muestra tal cual.
    if (err.status === 403 && codigoOt && typeof body?.message === "string" && body.message.trim()) {
      return { status: 403, code, message: body.message, body };
    }
    if (err.status === 403) {
      return {
        status: 403,
        code,
        message: "Solo el superadministrador con el permiso de Persona Natural puede hacer este cambio.",
        body,
      };
    }
    if (err.status >= 500) return { status: err.status, code, message: ERROR_DE_SERVIDOR, body };
    const serverMessage = typeof body?.message === "string" && body.message.trim() ? body.message : null;
    return { status: err.status, code, message: serverMessage ?? fallback, body };
  }
  if (err instanceof TypeError) return { status: null, code: null, message: SIN_CONEXION, body: null };
  return { status: null, code: null, message: fallback, body: null };
}

export function pnErrorMessage(err: unknown, fallback?: string): string {
  return pnError(err, fallback).message;
}
