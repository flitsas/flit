// Paso Envío (HU F6). Módulo puro, sin React ni red: el seguimiento tras radicar y las reglas de reintento del envío.
import type { DefinicionTipo, EstadoSolicitud } from "@/lib/api/cliente";
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
 * Seguimiento del envío para `RadicadoTracking`, a partir del estado de la solicitud. Los pasos son los del trámite entregado a
 * FLIT 2 como borrador: no hay revisión ni aprobación en esta entrega, así que no se muestran.
 * `actualizadoEn` es la fecha del último cambio de estado: es la del envío si sigue en cola y la de la creación si ya fue creada.
 */
export function pasosDeSeguimiento(estado: Pick<EstadoSolicitud, "estado" | "actualizadoEn">, definicion: DefinicionTipo): PasoVisible[] {
  const fecha = formatearFechaHora(estado.actualizadoEn);
  const validaIdentidad = definicion.flags.requiereBiometria || definicion.actores.some((a) => a.biometria);
  const creada = estado.estado === "creada";
  const rechazada = estado.estado === "rechazada_creacion";
  return [
    { titulo: "Borrador del trámite", estado: "done", fecha: null },
    { titulo: "Validación de identidad", estado: validaIdentidad ? "done" : "skipped", fecha: null },
    { titulo: "Enviado a FLIT 2", estado: "done", fecha: estado.estado === "en_cola" ? fecha : null },
    { titulo: "Creado en FLIT 2", estado: creada ? "done" : rechazada ? "rejected" : "current", fecha: creada || rechazada ? fecha : null },
  ];
}
