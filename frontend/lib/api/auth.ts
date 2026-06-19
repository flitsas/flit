// Funciones de la API de autenticación (HU #10168-#10171, #10203). Envuelven apiFetch
// para los flujos de login, recuperación, cambio y administración de credenciales.
import { apiFetch } from "./client";

export interface LoginResult {
  accessToken: string;
  expiresInSeconds: number;
  tokenType: string;
}

/** POST /api/v1/auth/login → JWT 12h. */
export function loginUser(email: string, password: string): Promise<LoginResult> {
  return apiFetch<LoginResult>("/api/v1/auth/login", {
    method: "POST",
    body: { email, password },
  });
}

/** POST /api/v1/auth/forgot-password → 202 genérico. */
export function forgotPassword(email: string): Promise<void> {
  return apiFetch<void>("/api/v1/auth/forgot-password", {
    method: "POST",
    body: { email },
  });
}

/** POST /api/v1/auth/reset-password → 200 | 400 (token inválido / política). */
export function resetPassword(token: string, newPassword: string): Promise<void> {
  return apiFetch<void>("/api/v1/auth/reset-password", {
    method: "POST",
    body: { token, newPassword },
  });
}

/** POST /api/v1/auth/remember-username → 202 genérico. */
export function rememberUsername(documentNumber: string): Promise<void> {
  return apiFetch<void>("/api/v1/auth/remember-username", {
    method: "POST",
    body: { documentNumber },
  });
}

/** PUT /api/v1/auth/change-password → 200 | 400 (política / actual incorrecta). */
export function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  return apiFetch<void>("/api/v1/auth/change-password", {
    method: "PUT",
    body: { currentPassword, newPassword },
  });
}

/** POST /api/v1/auth/admin/reset-password → 200 | 403 (fuera de ámbito) | 404. */
export function adminResetPassword(email: string): Promise<void> {
  return apiFetch<void>("/api/v1/auth/admin/reset-password", {
    method: "POST",
    body: { email },
  });
}
