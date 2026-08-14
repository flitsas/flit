/**
 * Mensaje visible unificado para el conflicto de correo ya asociado a otra
 * cuenta (HU #11550, código único desde HU #11580). Espejo EXACTO del literal
 * que ya envía el backend en
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
 *
 * HU #11580: el backend antes exponía tres códigos distintos
 * (`INVITATION_ALREADY_PENDING`, `USER_ALREADY_EXISTS`,
 * `EMAIL_BELONGS_TO_DELETED_USER`), lo que permitía a quien llama a la API
 * deducir por qué está ocupado el correo — las dos últimas comprobaciones son
 * globales, no por tenant, así que la información cruzaba la frontera del
 * tenant. El backend los colapsó en un único código `EMAIL_ALREADY_IN_USE`
 * (409, mismo mensaje) para que la respuesta sea indistinguible.
 */
export const EMAIL_ALREADY_ASSOCIATED_MESSAGE =
  "El correo utilizado ya se encuentra asociado a otra cuenta";

/**
 * Código de error que representa el conflicto: el correo ya está asociado a
 * otra cuenta. Debe coincidir con el `catch` que usa
 * `UserEmailConflictMessages` en el backend.
 */
const EMAIL_CONFLICT_CODES = new Set<string>(["EMAIL_ALREADY_IN_USE"]);

/**
 * Extrae el código de error del cuerpo JSON de una respuesta de error del backend.
 * Acepta tanto `code` (`SecurityEndpoints`, `ErrorResponse(Code, Message)`) como
 * `error` (`AdminOtEndpoints`, objeto anónimo `{ error, message }`) — mismo
 * significado, nombre de campo distinto según el endpoint que respondió.
 *
 * Uso de ejemplo: `emailConflictErrorCode({ error: "EMAIL_ALREADY_IN_USE" })` → `"EMAIL_ALREADY_IN_USE"`.
 */
export function emailConflictErrorCode(body: unknown): string | undefined {
  const parsed = body as { code?: string; error?: string } | null | undefined;
  return parsed?.code ?? parsed?.error;
}

/**
 * `true` si el código corresponde al conflicto de correo ya asociado a otra
 * cuenta. Desde la HU #11580 es UN solo código: los tres anteriores fueron
 * retirados a propósito y ya no se reconocen — si alguno vuelve a aparecer aquí,
 * se reabre la fuga que esa HU cerró.
 *
 * Uso de ejemplo: `isEmailConflictCode("EMAIL_ALREADY_IN_USE")` → `true`;
 * `isEmailConflictCode("USER_ALREADY_EXISTS")` → `false` (código retirado).
 */
export function isEmailConflictCode(code: string | undefined): boolean {
  return code !== undefined && EMAIL_CONFLICT_CODES.has(code);
}
