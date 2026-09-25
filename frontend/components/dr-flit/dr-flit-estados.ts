import { ESTADO_LABELS, type EstadoTramite } from "@/lib/tramites/estados";

/**
 * HU-E — qué significa cada estado para quien pregunta en el chat y qué sigue. Complementa el
 * chip (que solo dice el nombre, `ESTADO_LABELS`) con una línea de acción. Los estados de la ruta
 * de placa y la revocatoria (ADR-0059, Feature #12565) son los que más confunden porque no existían
 * cuando el usuario aprendió el flujo.
 *
 * Los estados terminales sin acción (`aprobado`, `anulado`) no llevan pista: el chip basta.
 */
const ESTADO_HINT: Partial<Record<EstadoTramite, string>> = {
  borrador: "Aún no radicado: complétalo y radícalo desde el detalle.",
  preparado: "Listo para radicar.",
  preasignacion: "Radicado sin placa: el organismo de tránsito debe asignarla.",
  asignado: "Placa asignada: gestiona SOAT e impuestos y envíalo al organismo.",
  entregado: "En revisión del organismo de tránsito.",
  rechazado: "Rechazado por el organismo: revisa el motivo en el detalle.",
  subsanacion: "Requiere subsanación: corrige lo indicado y reenvíalo.",
  revocado: "Aprobación revocada: consulta el detalle o la vista Revocatorias.",
};

function isEstadoTramite(value: string): value is EstadoTramite {
  return Object.prototype.hasOwnProperty.call(ESTADO_LABELS, value);
}

/** Pista de acción para un estado; `null` si no aplica o el estado es desconocido. */
export function estadoHint(estado: string | null | undefined): string | null {
  const key = (estado ?? "").trim().toLowerCase();
  if (!key || !isEstadoTramite(key)) return null;
  return ESTADO_HINT[key] ?? null;
}
