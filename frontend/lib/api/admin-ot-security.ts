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
  /** HU #10623/#10624: fecha de soft-delete. `null`/ausente en el listado normal;
   *  siempre poblado cuando se listó con `fetchOtUsers(scope, signal, true)` (onlyDeleted=true). */
  deletedAt?: string | null;
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

/** `endsAt` nulo (HU #10619 AC1) desactiva indefinidamente; con fecha, suspende temporalmente. */
export interface SuspendOtUserRequest {
  reason: string;
  endsAt: string | null;
}

/** HU #10621: payload de edición — displayName/email opcionales ("no tocar ese campo");
 *  rowVersion obligatorio (concurrencia optimista, AC3). */
export interface UpdateOtUserRequest {
  displayName?: string;
  email?: string;
  rowVersion: number;
}

/** HU #10623: payload de eliminación — rowVersion obligatorio (concurrencia optimista). */
export interface DeleteOtUserRequest {
  rowVersion: number;
}

/** Resultado de reenviar una invitación pendiente (HU #10626) — mismo shape que
 *  ResendInvitationResult (security.ts). */
export interface ResendOtInvitationResponse {
  invitationId: string;
  email: string;
  emailSent: boolean;
}

function scopeQuery(scope?: OtApiScope) {
  return scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined;
}

/** GET /api/v1/admin/ot/users — usuarios (activos/pendientes) del tenant OT resuelto. Con
 *  `onlyDeleted=true` (HU #10624) lista en su lugar los usuarios eliminados del mismo tenant OT
 *  resuelto — EXCLUSIVO de SuperAdmin (403 para ot_admin). Omitido o `false`: comportamiento
 *  normal, sin cambios. */
export function fetchOtUsers(
  scope?: OtApiScope,
  signal?: AbortSignal,
  onlyDeleted?: boolean,
): Promise<OtUserListResponse> {
  return apiFetch<OtUserListResponse>(`${base}/users`, {
    query: { ...scopeQuery(scope), onlyDeleted: onlyDeleted || undefined },
    signal,
  });
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

/** DELETE /api/v1/admin/ot/users/{userId} — elimina (soft-delete reversible) a un usuario del
 *  tenant OT resuelto (HU #10623). `rowVersion` obligatorio (concurrencia optimista). Errores
 *  mapeados por el backend con el campo `error` (no `code`, a diferencia de security.ts):
 *  400 SELF_DELETE, 409 LAST_ACTIVE_ADMIN | CONCURRENCY_CONFLICT, 404 si ya no existe. */
export function deleteOtUser(
  userId: string,
  body: DeleteOtUserRequest,
  scope?: OtApiScope,
): Promise<void> {
  return apiFetch<void>(`${base}/users/${userId}`, {
    method: "DELETE",
    body,
    query: scopeQuery(scope),
  });
}

/** POST /api/v1/admin/ot/invitations/{invitationId}/resend — reenvía una invitación pendiente del
 *  tenant OT resuelto (propio para ot_admin, o el indicado por `scope.transitOfficeId` para
 *  SuperAdmin) — HU #10626. SIEMPRE regenera el token de activación y reenvía el correo. El `id`
 *  de la fila YA es el `invitationId` cuando `status === "pending"` (`OtUserItem.id`). Errores:
 *  409 si la invitación ya no está pendiente; 429 con `{ error, message, retryAfterSeconds }` si
 *  el cooldown anti-abuso (~2 min) sigue activo (AC2) — este endpoint usa `error` (no `code`, a
 *  diferencia de security.ts — misma inconsistencia preexistente que deleteOtUser). */
export function resendOtInvitation(
  invitationId: string,
  scope?: OtApiScope,
): Promise<ResendOtInvitationResponse> {
  return apiFetch<ResendOtInvitationResponse>(`${base}/invitations/${invitationId}/resend`, {
    method: "POST",
    query: scopeQuery(scope),
  });
}

/** DELETE /api/v1/admin/ot/invitations/{invitationId} — cancela (anula) una invitación
 *  pendiente del tenant OT resuelto (propio para ot_admin, o el indicado por
 *  `scope.transitOfficeId` para SuperAdmin) — HU #10627/#10628. El enlace de activación
 *  anterior deja de funcionar y el email queda disponible para una nueva invitación. El `id`
 *  de la fila YA es el `invitationId` cuando `status === "pending"` (`OtUserItem.id`). Errores:
 *  404 si la invitación no existe en el tenant OT resuelto; 409 si ya no está pendiente (fue
 *  aceptada o cancelada previamente — condición de carrera, AC3). Este endpoint usa el campo
 *  `error` (no `code`, misma inconsistencia preexistente que resendOtInvitation/deleteOtUser).
 *  Sin cuerpo de petición ni de respuesta (204). */
export function cancelOtInvitation(invitationId: string, scope?: OtApiScope): Promise<void> {
  return apiFetch<void>(`${base}/invitations/${invitationId}`, {
    method: "DELETE",
    query: scopeQuery(scope),
  });
}
