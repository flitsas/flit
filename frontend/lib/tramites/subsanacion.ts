/**
 * HU #10874 — parseo cliente del checklist HÍBRIDO de subsanación que el backend
 * persiste en `procedure_instance_status_history.metadata` (ver `SubsanacionObservation`
 * en `Flit.Tramites.Domain.Tramites.ValueObjects`, HU #10871/#10872).
 *
 * Contrato vigente: `GET /instances/{id}` (`ProcedureInstanceStatusHistoryDto.metadata`)
 * expone la observación filtrada a `{motivo, items:[{campo,detalle}]}` (HU #10871, commit
 * f3b64f5e). Por seguridad/Habeas Data el backend NO envía `fieldSnapshot` ni los tenant ids
 * al cliente; el campo `fieldSnapshot` de abajo se conserva solo como parseo DEFENSIVO
 * (siempre llegará ausente → `null`). Si no hay observación estructurada, `SubsanacionPanel`
 * degrada al `reason` plano como motivo.
 */

import type { InstanceStatus, StatusHistory } from '@/lib/api/types/procedure-runtime';

export interface SubsanacionObservationItem {
  campo: string | null;
  detalle: string | null;
}

export interface SubsanacionObservation {
  motivo: string | null;
  items: SubsanacionObservationItem[];
  fieldSnapshot: Record<string, string | null> | null;
}

const ESTADO_SUBSANACION: InstanceStatus = 'subsanacion';
const ESTADO_RECHAZADO: InstanceStatus = 'rechazado';

/**
 * Motivo del rechazo del Organismo de Tránsito: `reason` de la última transición REAL a
 * `rechazado` (p. ej. entregado→rechazado). Excluye `rechazado→rechazado` (activar subsanación),
 * para no mostrar textos del operador. `null` si no hay un rechazo con motivo en el historial.
 */
export function latestRejectionReason(
  history: StatusHistory[] | null | undefined,
): string | null {
  if (!history || history.length === 0) return null;
  const entries = history.filter(
    (h) =>
      h.toStatus === ESTADO_RECHAZADO &&
      h.fromStatus !== ESTADO_RECHAZADO &&
      !!h.reason?.trim(),
  );
  if (entries.length === 0) return null;
  const latest = entries.reduce((a, b) =>
    new Date(b.changedAt).getTime() >= new Date(a.changedAt).getTime() ? b : a,
  );
  return latest.reason?.trim() ?? null;
}

/**
 * Última entrada del historial con observación de subsanación: legado `toStatus=subsanacion`,
 * o `rechazado`/`rechazado` con metadata de checklist/motivo estructurado.
 */
export function latestSubsanacionEntry(
  history: StatusHistory[] | null | undefined,
): StatusHistory | null {
  if (!history || history.length === 0) return null;
  const entries = history.filter((h) => {
    if (h.toStatus === ESTADO_SUBSANACION) return true;
    if (h.toStatus === ESTADO_RECHAZADO && parseSubsanacionObservation(h.metadata)) return true;
    return false;
  });
  if (entries.length === 0) return null;
  return entries.reduce((latest, current) =>
    new Date(current.changedAt).getTime() >= new Date(latest.changedAt).getTime() ? current : latest,
  );
}

/**
 * Deserialización tolerante del JSON híbrido de `metadata` (camelCase, espejo de
 * `SubsanacionObservation.ToJson()` en backend). Cualquier forma inesperada (null/vacío/corrupto/
 * sin las claves esperadas) degrada a `null` sin romper el render.
 */
export function parseSubsanacionObservation(
  metadata: string | null | undefined,
): SubsanacionObservation | null {
  if (!metadata || !metadata.trim()) return null;

  try {
    const raw = JSON.parse(metadata) as {
      motivo?: unknown;
      items?: unknown;
      fieldSnapshot?: unknown;
    };

    const items: SubsanacionObservationItem[] = Array.isArray(raw.items)
      ? raw.items
          .filter((it): it is Record<string, unknown> => !!it && typeof it === 'object')
          .map((it) => ({
            campo: typeof it.campo === 'string' ? it.campo : null,
            detalle: typeof it.detalle === 'string' ? it.detalle : null,
          }))
      : [];

    const fieldSnapshot =
      raw.fieldSnapshot && typeof raw.fieldSnapshot === 'object'
        ? (raw.fieldSnapshot as Record<string, string | null>)
        : null;

    return {
      motivo: typeof raw.motivo === 'string' ? raw.motivo : null,
      items,
      fieldSnapshot,
    };
  } catch {
    return null;
  }
}
