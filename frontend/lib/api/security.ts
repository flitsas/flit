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

/** GET /api/v1/security/users → lista de usuarios del tenant. */
export async function getUsers(): Promise<TenantUser[]> {
  return apiFetch<TenantUser[]>("/api/v1/security/users");
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

/** Tipo de entidad objetivo para filtrar el catálogo de módulos (HU #10504). */
export type ModulesTargetEntityType = "COMPANY" | "TRANSIT_OFFICE";

/** GET /api/v1/security/modules → módulos accesibles según permisos del caller.
 *  Si se pasa `targetEntityType`, el backend excluye los módulos scoped (vía Empresas)
 *  únicamente al otro tipo de entidad — los módulos sin scope configurado siempre aparecen. */
export async function getAccessibleModules(
  targetEntityType?: ModulesTargetEntityType,
): Promise<AccessibleModule[]> {
  return apiFetch<AccessibleModule[]>("/api/v1/security/modules", {
    query: { targetEntityType },
  });
}

/** POST /api/v1/security/roles → AdminCompany crea rol en su empresa. */
export async function createTenantRole(
  code: string,
  name: string,
  description?: string,
): Promise<{ id: string }> {
  return apiFetch<{ id: string }>("/api/v1/security/roles", {
    method: "POST",
    body: { code, name, description },
  });
}

/** PUT /api/v1/security/roles/{roleId}/permissions → AdminCompany asigna permisos (subset del propio). */
export async function setTenantRolePermissions(
  roleId: string,
  permissionIds: string[],
): Promise<RoleDetail> {
  return apiFetch<RoleDetail>(`/api/v1/security/roles/${roleId}/permissions`, {
    method: "PUT",
    body: { permissionIds },
  });
}

/** DELETE /api/v1/security/roles/{roleId} → AdminCompany elimina rol no-sistema de su empresa. */
export async function deleteTenantRole(roleId: string): Promise<void> {
  return apiFetch<void>(`/api/v1/security/roles/${roleId}`, {
    method: "DELETE",
  });
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
