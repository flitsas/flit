// Cliente tipado de la placa del trámite en estado `preasignacion`/`asignado` (ADR-0059, HU #10654,
// HU #12167, HU #12598). La consola de rangos (Feature #10587) se retiró en la Feature #12846
// (Épica #12751, HU-A1 backend / HU-A2 frontend): el backend responde 410 Gone para listar/crear/
// editar rangos, listar placas, elegibles y bloquear/desbloquear/revocar placa de rango, y
// `AssignPlateToProcedureAsync` siempre reserva fuera de rango sin importar lo que envíe el
// cliente. Solo sobreviven las rutas del ciclo de vida del trámite: assign-plate, release-plate y
// update-plate.
import { apiFetch } from "./client";

const base = "/api/v1/admin/plate-ranges";

/**
 * HU #10800 — asigna una placa al trámite en `preasignacion`.
 * HU #12850 (Feature #12846) — el backend (HU-A1) ignora cualquier `outOfRange` recibido y
 * siempre ejecuta `ReserveOutOfRangePlateAsync`; el cliente ya no ofrece elegir el modo, así que
 * el payload se fija en `outOfRange: true` de forma implícita, sin selector en la UI.
 */
export function assignPlateToProcedure(instanceId: string, plate: string): Promise<unknown> {
  return apiFetch(`${base}/procedures/${instanceId}/assign-plate`, {
    method: "POST",
    body: { plate, outOfRange: true },
  });
}

/** ADR-0059 (HU #12602) — «Liberar placa»: asignado → preasignacion; la placa sigue en el expediente. */
export function releaseProcedurePlate(instanceId: string, reason: string): Promise<unknown> {
  return apiFetch(`${base}/procedures/${instanceId}/release-plate`, { method: "POST", body: { reason } });
}

/** HU #12167 (Feature #12156) — corrige la placa dentro de la hora siguiente a la asignación (una única vez). */
export function updateProcedurePlate(instanceId: string, plate: string): Promise<unknown> {
  return apiFetch(`${base}/procedures/${instanceId}/update-plate`, { method: "POST", body: { plate } });
}
