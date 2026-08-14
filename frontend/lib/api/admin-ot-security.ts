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
  /** HU #11552 / ADR-0048: "cancelled" es un cuarto valor — invitación cancelada, visible y
   *  reactivable. El `id` de la fila sigue siendo el `invitationId` en "pending" y "cancelled". */
  status: "active" | "inactive" | "pending" | "cancelled";
  createdAt: string | null;
  isSuspended: boolean;
  /** HU #10621: versiÃ³n de concurrencia optimista, obligatoria al editar (PATCH). */
  rowVersion: number;
  /** HU #10623/#10624: fecha de soft-delete. `null`/ausente en el listado normal;
   *  siempre poblado cuando se listÃ³ con `fetchOtUsers(scope, signal, true)` (onlyDeleted=true). */
  deletedAt?: string | null;
}

export interface OtUserListResponse {
  data: OtUserItem[];
}

export interface InviteOtUserRequest {
  email: string;
  fullName?: string;
  /**
   * Roles del catálogo TRANSIT_OFFICE con los que entra el usuario. Si se omite, el backend
   * conserva el comportamiento histórico y asigna el rol de sistema `ot_admin`.
   */
  roleIds?: string[];
}

export interface InviteOtUserResponse {
  invitationId: string;
  email: string;
  emailSent: boolean;
}

/** `endsAt` nulo = desactivaciÃ³n indefinida (HU #10619/#10620); con valor = suspensiÃ³n
 *  temporal hasta esa fecha. */
export interface SuspendOtUserRequest {
  reason: string;
  endsAt: string | null;
}

/** HU #10621: payload de ediciÃ³n â€” displayName/email opcionales ("no tocar ese campo");
 *  rowVersion obligatorio (concurrencia optimista, AC3). */
export interface UpdateOtUserRequest {
  displayName?: string;
  email?: string;
  rowVersion: number;
}

/** HU #10623: payload de eliminaciÃ³n â€” rowVersion obligatorio (concurrencia optimista). */
export interface DeleteOtUserRequest {
  rowVersion: number;
}

/** Resultado de reenviar una invitaciÃ³n pendiente (HU #10626) â€” mismo shape que
 *  ResendInvitationResult (security.ts). */
export interface ResendOtInvitationResponse {
  invitationId: string;
  email: string;
  emailSent: boolean;
}


function scopeQuery(scope?: OtApiScope) {
  return scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined;
}

/** GET /api/v1/admin/ot/users â€” usuarios (activos/pendientes) del tenant OT resuelto. Con
 *  `onlyDeleted=true` (HU #10624) lista en su lugar los usuarios eliminados del mismo tenant OT
 *  resuelto â€” EXCLUSIVO de SuperAdmin (403 para ot_admin). Omitido o `false`: comportamiento
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

/** POST /api/v1/admin/ot/users/invite â€” invita un usuario con el rol ot_admin del tenant resuelto. */
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

/** POST /api/v1/admin/ot/users/{userId}/suspend â€” suspende temporalmente al usuario. */
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

/** DELETE /api/v1/admin/ot/users/{userId}/suspend â€” levanta la suspensiÃ³n activa. */
export function unsuspendOtUser(userId: string, scope?: OtApiScope): Promise<void> {
  return apiFetch<void>(`${base}/users/${userId}/suspend`, {
    method: "DELETE",
    query: scopeQuery(scope),
  });
}

/** PATCH /api/v1/admin/ot/users/{userId} â€” edita nombre y/o correo del usuario (HU #10621).
 *  409 con cÃ³digo USER_ALREADY_EXISTS | EMAIL_BELONGS_TO_DELETED_USER | CONCURRENCY_CONFLICT
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

/** DELETE /api/v1/admin/ot/users/{userId} â€” elimina (soft-delete reversible) a un usuario del
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

/** POST /api/v1/admin/ot/invitations/{invitationId}/resend â€” reenvÃ­a una invitaciÃ³n pendiente del
 *  tenant OT resuelto (propio para ot_admin, o el indicado por `scope.transitOfficeId` para
 *  SuperAdmin) â€” HU #10626. SIEMPRE regenera el token de activaciÃ³n y reenvÃ­a el correo. El `id`
 *  de la fila YA es el `invitationId` cuando `status === "pending"` (`OtUserItem.id`). Errores:
 *  409 si la invitaciÃ³n ya no estÃ¡ pendiente; 429 con `{ error, message, retryAfterSeconds }` si
 *  el cooldown anti-abuso (~2 min) sigue activo (AC2) â€” este endpoint usa `error` (no `code`, a
 *  diferencia de security.ts â€” misma inconsistencia preexistente que deleteOtUser). */
export function resendOtInvitation(
  invitationId: string,
  scope?: OtApiScope,
): Promise<ResendOtInvitationResponse> {
  return apiFetch<ResendOtInvitationResponse>(`${base}/invitations/${invitationId}/resend`, {
    method: "POST",
    query: scopeQuery(scope),
  });
}

/** DELETE /api/v1/admin/ot/invitations/{invitationId} â€” cancela (anula) una invitaciÃ³n
 *  pendiente del tenant OT resuelto (propio para ot_admin, o el indicado por
 *  `scope.transitOfficeId` para SuperAdmin) â€” HU #10627/#10628. El enlace de activaciÃ³n
 *  anterior deja de funcionar y el email queda disponible para una nueva invitaciÃ³n. El `id`
 *  de la fila YA es el `invitationId` cuando `status === "pending"` (`OtUserItem.id`). Errores:
 *  404 si la invitaciÃ³n no existe en el tenant OT resuelto; 409 si ya no estÃ¡ pendiente (fue
 *  aceptada o cancelada previamente â€” condiciÃ³n de carrera, AC3). Este endpoint usa el campo
 *  `error` (no `code`, misma inconsistencia preexistente que resendOtInvitation/deleteOtUser).
 *  Sin cuerpo de peticiÃ³n ni de respuesta (204). */
export function cancelOtInvitation(invitationId: string, scope?: OtApiScope): Promise<void> {
  return apiFetch<void>(`${base}/invitations/${invitationId}`, {
    method: "DELETE",
    query: scopeQuery(scope),
  });
}

/** Resultado de reactivar una invitación cancelada (HU #11552) — mismo shape que
 *  ResendOtInvitationResponse. */
export interface ReactivateOtInvitationResponse {
  invitationId: string;
  email: string;
  emailSent: boolean;
}

/** POST /api/v1/admin/ot/invitations/{invitationId}/reactivate â€” reactiva una invitaciÃ³n
 *  cancelada del tenant OT resuelto (propio para ot_admin, o el indicado por
 *  `scope.transitOfficeId` para SuperAdmin) â€” HU #11552 / ADR-0048. SIEMPRE regenera el token
 *  de activaciÃ³n y reenvÃ­a el correo. El `id` de la fila YA es el `invitationId` cuando
 *  `status === "cancelled"` (`OtUserItem.id`). Sin cuerpo de peticiÃ³n. Errores: 404 si no
 *  existe en el tenant OT resuelto; 409 INVITATION_NOT_CANCELLED si ya no estÃ¡ cancelada, o el
 *  mismo trÃ­o de conflictos de correo que crear invitaciÃ³n; 429 con
 *  `{ error, message, retryAfterSeconds }` si el cooldown anti-abuso sigue activo â€” este
 *  endpoint usa `error` (no `code`, misma inconsistencia preexistente que resendOtInvitation).
 */
export function reactivateOtInvitation(
  invitationId: string,
  scope?: OtApiScope,
): Promise<ReactivateOtInvitationResponse> {
  return apiFetch<ReactivateOtInvitationResponse>(
    `${base}/invitations/${invitationId}/reactivate`,
    { method: "POST", query: scopeQuery(scope) },
  );
}
