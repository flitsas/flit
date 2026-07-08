import { apiFetch } from "./client";
import { ApiError } from "./types";

export interface InvitationCreatedResult {
  invitationId: string;
  email: string;
  emailSent: boolean;
}

export interface TenantUser {
  id: string;
  fullName: string;
  email: string;
  role: string | null;
  roleCode: string | null;
  roleId: string | null;
  status: "active" | "inactive" | "pending";
  createdAt: string | null;
  isSuspended: boolean;
  tenantId?: string | null;
  tenantName?: string | null;
  /** HU #10621: versión de concurrencia optimista, obligatoria al editar (PATCH). */
  rowVersion: number;
  /** HU #10623/#10624: fecha de soft-delete. `null`/ausente en el listado normal;
   *  siempre poblado cuando se listó con `getUsers(true)` (onlyDeleted=true). */
  deletedAt?: string | null;
}

/** HU #10621: payload de edición — displayName/email opcionales ("no tocar ese campo");
 *  rowVersion obligatorio (concurrencia optimista, AC3). */
export interface UpdateUserRequest {
  displayName?: string;
  email?: string;
  rowVersion: number;
}

export interface TenantRole {
  id: string;
  code: string;
  name: string;
  description: string | null;
  isSystem: boolean;
  permissionCount: number;
  createdAt: string;
}

export interface AccessibleAction {
  id: string;
  slug: string;
  name: string;
}

export interface AccessibleModule {
  id: string;
  code: string;
  name: string;
  sortOrder: number;
  actions: AccessibleAction[];
}

export interface RoleDetail {
  id: string;
  tenantId: string;
  code: string;
  name: string;
  description: string | null;
  isSystem: boolean;
  isActive: boolean;
  permissions: { id: string; slug: string; name: string }[];
}

/** POST /api/v1/security/invitations → 201 | 400 (NO_ROLES_SELECTED) | 404 (rol) | 409 (pending duplicado).
 *  SuperAdmin puede pasar targetTenantId para invitar a otro tenant (en ese caso el rol de
 *  sistema lo resuelve el backend y `roleIds` se ignora — se puede enviar `[]`).
 *  HU #10510: `roleIds` reemplaza el `roleId?` singular — selección múltiple, mínimo 1 rol
 *  para AdminCompany/OtAdmin (el backend rechaza con NO_ROLES_SELECTED si viene vacío). */
export async function createInvitation(
  email: string,
  fullName: string,
  roleIds: string[],
  targetTenantId?: string,
): Promise<InvitationCreatedResult> {
  try {
    return await apiFetch<InvitationCreatedResult>("/api/v1/security/invitations", {
      method: "POST",
      body: {
        email,
        fullName,
        roleIds,
        targetTenantId: targetTenantId || undefined,
      },
    });
  } catch (err) {
    if (err instanceof ApiError) throw err;
    throw err;
  }
}

/** GET /api/v1/security/users → lista de usuarios del tenant (AdminCompany) o de todas las
 *  compañías (SuperAdmin, excepto su tenant interno). Con `onlyDeleted=true` (HU #10624) lista en
 *  su lugar los usuarios eliminados (soft-delete) de CUALQUIER tenant — EXCLUSIVO de SuperAdmin
 *  (403 en otro caso). Omitido o `false`: comportamiento normal, sin cambios. */
export async function getUsers(onlyDeleted?: boolean): Promise<TenantUser[]> {
  return apiFetch<TenantUser[]>("/api/v1/security/users", {
    query: onlyDeleted ? { onlyDeleted: true } : undefined,
  });
}

/** GET /api/v1/security/roles */
export async function getRoles(): Promise<TenantRole[]> {
  return apiFetch<TenantRole[]>("/api/v1/security/roles");
}

/** PUT /api/v1/security/users/{userId}/role */
export async function assignRole(userId: string, roleId: string): Promise<void> {
  return apiFetch<void>(`/api/v1/security/users/${userId}/role`, {
    method: "PUT",
    body: { roleId },
  });
}

/** GET /api/v1/security/modules → módulos accesibles según permisos del caller.
 *  RBAC puro (HU #10664): los módulos son transversales; el SuperAdmin ve todos los módulos
 *  activos (constructor de roles) y el caller tenant solo los de sus slugs. */
export async function getAccessibleModules(): Promise<AccessibleModule[]> {
  return apiFetch<AccessibleModule[]>("/api/v1/security/modules");
}

/** Cuerpo del POST de suspensión/desactivación. `endsAt` nulo = desactivación indefinida
 *  (HU #10619/#10620); con valor = suspensión temporal hasta esa fecha. */
export interface SuspendUserRequest {
  reason: string;
  endsAt: string | null;
}

/** POST /api/v1/security/users/{userId}/suspend — suspende (temporal, con `endsAt`) o
 *  desactiva indefinidamente (`endsAt: null`) al usuario (HU #10619/#10620). */
export async function blockUser(
  userId: string,
  reason: string,
  endsAt: string | null,
): Promise<{ id: string }> {
  return apiFetch<{ id: string }>(`/api/v1/security/users/${userId}/suspend`, {
    method: "POST",
    body: { reason, endsAt },
  });
}

/** DELETE /api/v1/security/users/{userId}/suspend — levanta la suspensión activa. */
export async function unblockUser(userId: string): Promise<void> {
  return apiFetch<void>(`/api/v1/security/users/${userId}/suspend`, {
    method: "DELETE",
  });
}

/** PATCH /api/v1/security/users/{userId} — edita nombre y/o correo del usuario (HU #10621).
 *  409 con código USER_ALREADY_EXISTS | EMAIL_BELONGS_TO_DELETED_USER | CONCURRENCY_CONFLICT
 *  (rowVersion desactualizado); 404 si el usuario ya no existe. */
export async function updateUser(userId: string, request: UpdateUserRequest): Promise<void> {
  return apiFetch<void>(`/api/v1/security/users/${userId}`, {
    method: "PATCH",
    body: request,
  });
}

/** DELETE /api/v1/security/users/{userId} — elimina (soft-delete reversible) a un usuario
 *  dentro del alcance del caller (AdminCompany su propio tenant, SuperAdmin cualquiera) —
 *  HU #10623. `rowVersion` es obligatorio (concurrencia optimista, el valor leído de
 *  `TenantUser.rowVersion`). Errores mapeados por el backend: 400 SELF_DELETE, 409
 *  LAST_ACTIVE_ADMIN | CONCURRENCY_CONFLICT, 404 si el usuario ya no existe. NO toca roles
 *  ni suspensiones: restaurar (`restoreUser`, SOLO SuperAdmin) recupera el mismo estado. */
export async function deleteUser(userId: string, rowVersion: number): Promise<void> {
  return apiFetch<void>(`/api/v1/security/users/${userId}`, {
    method: "DELETE",
    body: { rowVersion },
  });
}

/** POST /api/v1/superadmin/users/{userId}/restore — restaura (deshace el soft-delete) a un
 *  usuario eliminado (HU #10623). SOLO SuperAdmin. No toca roles ni suspensiones: el usuario
 *  recupera exactamente el mismo estado que tenía al eliminarse. 409 USER_NOT_DELETED si el
 *  usuario no estaba eliminado; 404 si no existe. */
export async function restoreUser(userId: string): Promise<void> {
  return apiFetch<void>(`/api/v1/superadmin/users/${userId}/restore`, {
    method: "POST",
  });
}

/** Resultado de reenviar una invitación pendiente (HU #10626) — mismo shape en el endpoint
 *  Compañía y en el OT (ver admin-ot-security.ts). */
export interface ResendInvitationResult {
  invitationId: string;
  email: string;
  emailSent: boolean;
}

/** POST /api/v1/security/invitations/{invitationId}/resend — reenvía una invitación pendiente
 *  (HU #10626): SIEMPRE regenera el token de activación (invalida el enlace anterior) y reenvía
 *  el correo. Mismo alcance de autorización que crear invitaciones (AdminCompany su tenant,
 *  SuperAdmin cualquiera). El `id` de la fila YA es el `invitationId` cuando `status === "pending"`
 *  (`TenantUser.id`) — no hace falta un campo nuevo. Errores: 409 si la invitación ya no está
 *  pendiente (fue aceptada o cancelada); 429 con `{ code, message, retryAfterSeconds }` si el
 *  cooldown anti-abuso (~2 min, configurable) sigue activo (AC2) — este endpoint usa `code`
 *  (el de OT usa `error`, ver ResendInvitationButton). */
export async function resendInvitation(invitationId: string): Promise<ResendInvitationResult> {
  return apiFetch<ResendInvitationResult>(`/api/v1/security/invitations/${invitationId}/resend`, {
    method: "POST",
  });
}

/** DELETE /api/v1/security/invitations/{invitationId} — cancela (anula) una invitación
 *  pendiente (HU #10627/#10628): el enlace de activación anterior deja de funcionar y el
 *  email queda disponible de nuevo para una nueva invitación. Distinto de eliminar un usuario
 *  (`deleteUser`/`DeleteUserDialog`): aquí todavía no existe ninguna cuenta creada. Mismo
 *  alcance que crear invitaciones (AdminCompany su tenant, SuperAdmin cualquiera). El `id` de
 *  la fila YA es el `invitationId` cuando `status === "pending"` (`TenantUser.id`) — no hace
 *  falta un campo nuevo. Errores: 404 si la invitación ya no existe o no pertenece al alcance
 *  del caller; 409 si ya no está pendiente (fue aceptada o cancelada previamente por otra
 *  persona — condición de carrera, AC3). Sin cuerpo de petición ni de respuesta (204).
 */
export async function cancelInvitation(invitationId: string): Promise<void> {
  return apiFetch<void>(`/api/v1/security/invitations/${invitationId}`, {
    method: "DELETE",
  });
}
