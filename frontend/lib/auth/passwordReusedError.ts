/**
 * Mensaje y detección unificados para el 409 `PASSWORD_REUSED` que el backend
 * devuelve cuando la nueva contraseña coincide con la vigente (HU #11553).
 * Aplica a los dos flujos donde la contraseña la elige la persona:
 * `PUT /api/v1/auth/change-password` y `POST /api/v1/auth/reset-password`.
 *
 * Se centraliza aquí (en vez de repetir el literal en cada formulario) para que
 * ambos flujos queden consistentes y el texto viva en un solo sitio. No reutilizar
 * este módulo para `ResetPasswordDialog` (reset administrativo): ahí la
 * contraseña la genera el sistema, no la persona, y el mismo código significa
 * un fallo interno del generador, no una reutilización por parte del usuario.
 */
export const PASSWORD_REUSED_MESSAGE =
  "La nueva contraseña debe ser diferente a la actual. Elige una distinta.";

/**
 * `true` si el error corresponde al 409 `PASSWORD_REUSED`. Filtra por el código
 * del cuerpo, no solo por el status: un 409 con otro código no debe mostrar este
 * mensaje.
 *
 * Uso de ejemplo: `isPasswordReusedError(409, { code: "PASSWORD_REUSED" })` → `true`.
 */
export function isPasswordReusedError(status: number | undefined, body: unknown): boolean {
  if (status !== 409) {
    return false;
  }
  const code = (body as { code?: string } | null | undefined)?.code;
  return code === "PASSWORD_REUSED";
}
