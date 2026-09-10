/**
 * HU #11552 / ADR-0048: antes de esta HU, `status !== "pending"` era el guarda usado en TODO el
 * código para decidir "esta fila es un usuario real, no una invitación" (Editar, Restablecer
 * contraseña, Suspender/Desactivar, Eliminar, Ver historial). Con "cancelled" como un cuarto
 * valor de `status` visible en el listado, ese guarda deja de ser equivalente: una fila
 * "cancelled" también tiene `id === invitationId`, NO `userId`, y ofrecer esas acciones sobre
 * ella produce 404 (o peor).
 *
 * Este helper centraliza la condición real ("¿el id de esta fila es un invitationId?") para que
 * el compilador de TypeScript señale cada sitio que compara contra `"pending"` a secas cuando se
 * amplía la unión de `status`, en vez de confiar solo en un barrido manual de líneas.
 *
 * Uso de ejemplo: `isInvitationRow({ status: "cancelled" })` → `true`;
 * `!isInvitationRow(u)` sustituye a `u.status !== "pending"` en los guardas de acciones.
 */
export function isInvitationRow(row: { status: string }): boolean {
  return row.status === "pending" || row.status === "cancelled";
}
