/**
 * Mensaje visible unificado para los tres conflictos de correo ya asociado a otra
 * cuenta (HU #11550): invitación pendiente, cuenta activa existente y cuenta
 * eliminada. Espejo EXACTO del literal que ya envía el backend en
 * `services/core-api/src/Flit.Api/Endpoints/UserEmailConflictMessages.cs`
 * (`UserEmailConflictMessages.EmailAlreadyInUse`).
 *
 * Antes del ajuste, `InviteUserModal`, `OtUsersSection` y `EditUserModal` tenían
 * cada uno su propio literal — copiado y ya divergente entre sí — y SOBRESCRIBÍAN
 * el mensaje unificado que el backend ya manda. Se centraliza aquí en vez de
 * propagar `body.message` porque el nombre del campo del código de error NO es
 * consistente entre endpoints (`SecurityEndpoints` usa `code`, `AdminOtEndpoints`
 * usa `error`), así que de todos modos hace falta un punto único que normalice
 * ambos antes de decidir el mensaje.
 */
export const EMAIL_ALREADY_ASSOCIATED_MESSAGE =
  "El correo utilizado ya se encuentra asociado a otra cuenta";

/**
 * Códigos de error que representan el mismo conflicto: el correo ya está asociado
 * a otra cuenta (invitación pendiente, cuenta activa o cuenta eliminada). Deben
 * coincidir uno a uno con los `catch` que usan `UserEmailConflictMessages` en el
 * backend.
 */
const EMAIL_CONFLICT_CODES = new Set<string>([
  "INVITATION_ALREADY_PENDING",
  "USER_ALREADY_EXISTS",
  "EMAIL_BELONGS_TO_DELETED_USER",
]);

/**
 * Extrae el código de error del cuerpo JSON de una respuesta de error del backend.
 * Acepta tanto `code` (`SecurityEndpoints`, `ErrorResponse(Code, Message)`) como
 * `error` (`AdminOtEndpoints`, objeto anónimo `{ error, message }`) — mismo
 * significado, nombre de campo distinto según el endpoint que respondió.
 *
 * Uso de ejemplo: `emailConflictErrorCode({ code: "USER_ALREADY_EXISTS" })` → `"USER_ALREADY_EXISTS"`.
 */
export function emailConflictErrorCode(body: unknown): string | undefined {
  const parsed = body as { code?: string; error?: string } | null | undefined;
  return parsed?.code ?? parsed?.error;
}

/**
 * `true` si el código corresponde a uno de los tres conflictos de correo ya
 * asociado a otra cuenta.
 *
 * Uso de ejemplo: `isEmailConflictCode("EMAIL_BELONGS_TO_DELETED_USER")` → `true`.
 */
export function isEmailConflictCode(code: string | undefined): boolean {
  return code !== undefined && EMAIL_CONFLICT_CODES.has(code);
}
