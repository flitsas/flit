// Cliente tipado del self-service de usuarios OT (refactor adminOT). Mismo grupo
// `/api/v1/admin/ot/*` que `admin-ot.ts`, protegido por OtModulePolicy (SuperAdmin
// u ot_admin). El scope (`OtApiScope`) resuelve el tenant destino: ot_admin siempre
// usa su propio tenant (JWT); SuperAdmin debe indicar `transitOfficeId`.
import { apiFetch } from "./client";
import type { OtApiScope } from "./admin-ot";

const base = "/api/v1/admin/ot";

export interface OtUserItem {
  id: string;
  fullName: string;
  email: string;
  role: string | null;
  roleCode: string | null;
  roleId: string | null;
  status: "active" | "inactive" | "pending";
  createdAt: string | null;
  isSuspended: boolean;
  /** HU #10621: versión de concurrencia optimista, obligatoria al editar (PATCH). */
  rowVersion: number;
}

export interface OtUserListResponse {
  data: OtUserItem[];
}

export interface InviteOtUserRequest {
  email: string;
  fullName?: string;
}

export interface InviteOtUserResponse {
  invitationId: string;
  email: string;
  emailSent: boolean;
}

export interface SuspendOtUserRequest {
  reason: string;
  endsAt: string;
}

/** HU #10621: payload de edición — displayName/email opcionales ("no tocar ese campo");
 *  rowVersion obligatorio (concurrencia optimista, AC3). */
export interface UpdateOtUserRequest {
  displayName?: string;
  email?: string;
  rowVersion: number;
}

function scopeQuery(scope?: OtApiScope) {
  return scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined;
}

/** GET /api/v1/admin/ot/users — usuarios (activos/pendientes) del tenant OT resuelto. */
export function fetchOtUsers(scope?: OtApiScope, signal?: AbortSignal): Promise<OtUserListResponse> {
  return apiFetch<OtUserListResponse>(`${base}/users`, { query: scopeQuery(scope), signal });
}

/** POST /api/v1/admin/ot/users/invite — invita un usuario con el rol ot_admin del tenant resuelto. */
export function inviteOtUser(
  input: InviteOtUserRequest,
  scope?: OtApiScope,
): Promise<InviteOtUserResponse> {
  return apiFetch<InviteOtUserResponse>(`${base}/users/invite`, {
    method: "POST",
    body: input,
    query: scopeQuery(scope),
  });
}

/** POST /api/v1/admin/ot/users/{userId}/suspend — suspende temporalmente al usuario. */
export function suspendOtUser(
  userId: string,
  body: SuspendOtUserRequest,
  scope?: OtApiScope,
): Promise<{ id: string }> {
  return apiFetch<{ id: string }>(`${base}/users/${userId}/suspend`, {
    method: "POST",
    body,
    query: scopeQuery(scope),
  });
}

/** DELETE /api/v1/admin/ot/users/{userId}/suspend — levanta la suspensión activa. */
export function unsuspendOtUser(userId: string, scope?: OtApiScope): Promise<void> {
  return apiFetch<void>(`${base}/users/${userId}/suspend`, {
    method: "DELETE",
    query: scopeQuery(scope),
  });
}

/** PATCH /api/v1/admin/ot/users/{userId} — edita nombre y/o correo del usuario (HU #10621).
 *  409 con código USER_ALREADY_EXISTS | EMAIL_BELONGS_TO_DELETED_USER | CONCURRENCY_CONFLICT
 *  (rowVersion desactualizado); 404 si el usuario ya no existe. */
export function updateOtUser(
  userId: string,
  body: UpdateOtUserRequest,
  scope?: OtApiScope,
): Promise<void> {
  return apiFetch<void>(`${base}/users/${userId}`, {
    method: "PATCH",
    body,
    query: scopeQuery(scope),
  });
}
