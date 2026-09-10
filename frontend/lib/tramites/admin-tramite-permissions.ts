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
 * Estados destino válidos para "Cambiar estado" (HU #12159): todos los estados de negocio
 * conocidos MENOS `aprobado` — el backend rechaza con 422 si `aprobado` aparece como origen o
 * como destino (la aprobación exige el flujo formal del organismo de tránsito). No incluye
 * `subsanacion`: es un estado LEGADO que `TramiteEstado.Todos` (backend) ya excluye de la lista de
 * estados válidos para este endpoint.
 */
export const ADMIN_ESTADO_DESTINO_OPTIONS = [
  'borrador',
  'preparado',
  'entregado',
  'rechazado',
  'anulado',
] as const;
