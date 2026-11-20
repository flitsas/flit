// Paso Envío (HU F6). Módulo puro, sin React ni red: el seguimiento tras radicar y las reglas de reintento del envío.
import type { ActorEstado, DefinicionTipo, EstadoSolicitud } from "@/lib/api/cliente";
import { ApiError } from "@/lib/api/errores";
import type { PasoVisible } from "@/components/modales/RadicadoTracking";
import { formatearFechaHora } from "@/lib/formato";

/** Cada cuánto se pregunta el estado tras radicar: prudente, para no cargar al BFF (que además limita por IP). */
export const INTERVALO_SONDEO_MS = 12_000;

/** Estados del portal en los que ya no hay nada que esperar: se deja de sondear. */
export const ESTADOS_FINALES: readonly string[] = ["creada", "rechazada_creacion", "cancelada"];

export const esEstadoFinal = (estado: string): boolean => ESTADOS_FINALES.includes(estado);

/**
 * Cuando una petición de radicar falla, ¿se reutiliza la misma Idempotency-Key en el siguiente intento? Sí si no se sabe qué pasó
 * (sin red, 5xx, 429): así un envío que sí llegó no se duplica. No si el back respondió con un rechazo definitivo (422, 409…):
 * el siguiente intento es otro intento y la llave anterior podría estar ligada a otra solicitud.
 */
export function conservaLlave(error: unknown): boolean {
  if (!(error instanceof ApiError)) return true;
  return error.status >= 500 || error.status === 429;
}

/**
 * Verdad de la validación de identidad en la línea de tiempo: «done» solo si todas las partes que se validan están aprobadas;
 * «current» si alguna sigue en curso; si no, «pending». Sin la lista de actores (consulta pública) se mantiene «done».
 */
function estadoIdentidad(definicion: DefinicionTipo, actores: readonly ActorEstado[] | undefined): PasoVisible["estado"] {
  if (!actores) return "done";
  const roles = new Set(definicion.actores.filter((a) => a.biometria).map((a) => a.rol));
  const partes = roles.size > 0 ? actores.filter((a) => roles.has(a.rol)) : [...actores];
  if (partes.length > 0 && partes.every((a) => a.validacionEstado === "aprobada")) return "done";
  return partes.some((a) => a.validacionEstado === "pendiente" || a.validacionEstado === "pendiente_envio") ? "current" : "pending";
}

/**
 * Seguimiento del paso Envío, con los mismos cinco títulos del diseño (`trackingEnviado`): recién enviado, el paso en curso es
 * «Enviado al OT». `actualizadoEn` es la fecha de ese último cambio.
 */
export function pasosDeSeguimiento(estado: Pick<EstadoSolicitud, "estado" | "actualizadoEn">, definicion: DefinicionTipo, actores?: readonly ActorEstado[]): PasoVisible[] {
  const fecha = formatearFechaHora(estado.actualizadoEn);
  const validaIdentidad = definicion.flags.requiereBiometria || definicion.actores.some((a) => a.biometria);
  const creada = estado.estado === "creada";
  const enCola = estado.estado === "en_cola" || estado.estado === "rechazada_creacion";
  return [
    { titulo: "Borrador del trámite", estado: "done", fecha: null },
    { titulo: "Validación de identidad", estado: validaIdentidad ? estadoIdentidad(definicion, actores) : "skipped", fecha: null },
    { titulo: "Enviado al OT", estado: enCola ? "current" : "done", fecha: enCola ? fecha : null },
    { titulo: "En revisión", estado: creada ? "current" : "pending", fecha: creada ? fecha : null },
    { titulo: "Aprobado / Rechazado", estado: "pending", fecha: null },
  ];
}
