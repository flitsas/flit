/**
 * HU #13140 — textos de las acciones desactivar, reactivar y eliminar un mandatario: resultado de la
 * reasignación de trámites, conflicto al reactivar y mensajes de error del servidor (403 y 409).
 */
import {
  MANDATE_SIGNER_ERROR,
  mandateSignerErrorCode,
  type MandateSignerLifecycleOutcome,
  type MandateSignerReactivation,
} from "@/lib/api/admin-mandate-signers";
import { ApiError } from "@/lib/api/types";

export type AccionBaja = "eliminar" | "desactivar";

/** Mensaje de éxito de una baja; informa cuántos trámites se reasignaron y cuántos decide el OT. */
export function mensajeResultadoBaja(
  nombre: string,
  accion: AccionBaja,
  outcome: MandateSignerLifecycleOutcome | undefined,
): string {
  const base = accion === "eliminar" ? `${nombre} fue eliminado.` : `${nombre} quedó desactivado.`;
  const reasignados = outcome?.reassigned ?? 0;
  const pendientes = outcome?.pendingOtDecision ?? 0;
  let texto = base;
  if (reasignados > 0) {
    texto += reasignados === 1 ? " Se reasignó 1 trámite." : ` Se reasignaron ${reasignados} trámites.`;
  }
  if (pendientes > 0) {
    texto +=
      pendientes === 1
        ? " Queda 1 trámite para que el OT decida al aprobar."
        : ` Quedan ${pendientes} trámites para que el OT decida al aprobar.`;
  }
  return texto;
}

/** Mensaje de éxito de reactivar; avisa si no desplazó al mandatario que ya estaba vigente. */
export function mensajeResultadoReactivar(
  nombre: string,
  result: MandateSignerReactivation | undefined,
): string {
  const conflictos = result?.conflictLinks?.length ?? 0;
  if (conflictos === 0) return `${nombre} vuelve a estar activo.`;
  return (
    `${nombre} vuelve a estar activo, pero no desplazó al mandatario vigente: ` +
    `${conflictos === 1 ? "1 vínculo quedó" : `${conflictos} vínculos quedaron`} inactivo` +
    `${conflictos === 1 ? "" : "s"} porque ya hay otro activo para esa compañía y organismo.`
  );
}

/** Mensaje claro para un error de desactivar, reactivar o eliminar. */
export function mensajeErrorAccion(error: unknown): string {
  const code = mandateSignerErrorCode(error);
  if (error instanceof ApiError) {
    if (error.status === 403) {
      return code === MANDATE_SIGNER_ERROR.lockedByOffice
        ? "Este mandatario lo configuró el organismo de tránsito: solo el organismo puede modificarlo."
        : "No tienes permiso para esta acción.";
    }
    if (error.status === 409 && code === MANDATE_SIGNER_ERROR.activeExists) {
      return "Ya hay otro mandatario activo para esa compañía y organismo. Desactívalo antes de continuar.";
    }
    if (error.status === 404) {
      return "El mandatario ya no existe. Actualiza la lista.";
    }
  }
  return "No se pudo completar la acción. Inténtalo de nuevo.";
}
