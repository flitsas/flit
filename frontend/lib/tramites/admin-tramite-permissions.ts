/**
 * HU #12163 (Feature #12155) — slugs de permiso del menú de acciones avanzadas del administrador
 * sobre trámites del Dashboard. Espejo LITERAL de `AdminTramiteAuthorization` (backend,
 * `services/core-api/src/Flit.Api/Authorization/AdminTramiteAuthorization.cs`): el frontend SOLO
 * lee el claim `permissions` del JWT (vía `usePermissions`) para decidir qué acciones OFRECE el
 * menú — el backend sigue siendo la única autoridad real (403 si una llamada se cuela sin el slug).
 */
export const ADMIN_TRAMITE_PERMISSIONS = {
  limpiarConsolidado: 'AdminTramiteLimpiarConsolidado',
  cargarConsolidado: 'AdminTramiteCargarConsolidado',
  cambiarEstado: 'AdminTramiteCambiarEstado',
  anular: 'AdminTramiteAnular',
  reenviarValidacion: 'AdminTramiteReenviarValidacion',
  reasignarGestor: 'AdminTramiteReasignarGestor',
  // Feature #12276 — «Consultar ahora en RUNT»: la consulta manual de la Confirmación RUNT se ofrece
  // desde el menú del admin (no solo desde el Historial, donde un trámite nunca consultado no aparece).
  // Mismo slug que protege el endpoint POST /admin/runt-confirmation/procedures/{id}/consult-now.
  consultarRunt: 'runt_confirmation.history.read',
} as const;

/**
 * Estados destino válidos para "Cambiar estado" (HU #12159): todos los estados de negocio de
 * `TramiteEstado.Todos` (backend) MENOS `aprobado` — el backend rechaza con 422 si `aprobado` aparece
 * como origen o como destino (la aprobación exige el flujo formal del organismo de tránsito). No
 * incluye `subsanacion`: es un estado LEGADO que `TramiteEstado.Todos` ya excluye.
 *
 * Van en el orden del ciclo de vida. Incluye los de la ruta de placa (`preasignacion`, `asignado`,
 * ADR-0059) y `revocado`, que el backend ya aceptaba pero el selector no ofrecía. El cambio queda en el
 * historial del trámite (tracking) y en el evento `cambio_estado_admin`, igual que los demás.
 */
export const ADMIN_ESTADO_DESTINO_OPTIONS = [
  'borrador',
  'preparado',
  'preasignacion',
  'asignado',
  'entregado',
  'rechazado',
  'anulado',
  'revocado',
] as const;

export type AdminEstadoDestino = (typeof ADMIN_ESTADO_DESTINO_OPTIONS)[number];

/** Familia cuyos tipos piden placa al organismo: la única que recorre la ruta de placa. */
const FAMILIA_RUTA_PLACA = 'MATRICULAS';

const ESTADOS_SOLO_RUTA_PLACA: readonly string[] = ['preasignacion', 'asignado'];

/**
 * Opciones del selector «Nuevo estado» para un trámite: excluye el estado actual y, fuera de matrícula,
 * los de la ruta de placa (solo los alcanza un tipo que pide placa; en un traspaso dejarían el trámite en
 * un estado que su flujo no tiene). Sin familia conocida se ofrecen todos: el backend no los restringe.
 */
export function opcionesEstadoDestinoAdmin(
  estadoActual: string | null | undefined,
  familia: string | null | undefined,
): AdminEstadoDestino[] {
  const sinRutaPlaca = !!familia && familia.trim().toUpperCase() !== FAMILIA_RUTA_PLACA;
  return ADMIN_ESTADO_DESTINO_OPTIONS.filter(
    (e) => e !== estadoActual && !(sinRutaPlaca && ESTADOS_SOLO_RUTA_PLACA.includes(e)),
  );
}
