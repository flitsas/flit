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
