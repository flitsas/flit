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

/**
 * POST /api/v1/auth/admin/block-user → 200 | 403 (fuera de ámbito) | 404.
 * NOTA: el endpoint backend de bloqueo temporal está pendiente (HU backend separada);
 * el frontend ya maneja su contrato (incl. 403 fuera de ámbito, HU #10174 AC2).
 */
export function adminBlockUser(email: string, days: number): Promise<void> {
  return apiFetch<void>("/api/v1/auth/admin/block-user", {
    method: "POST",
    body: { email, days },
  });
}

/** POST /api/v1/auth/activate → 200 | 400 (INVITATION_INVALID | WEAK_PASSWORD). */
export function activateAccount(token: string, password: string): Promise<void> {
  return apiFetch<void>("/api/v1/auth/activate", {
    method: "POST",
    body: { token, password },
  });
}
