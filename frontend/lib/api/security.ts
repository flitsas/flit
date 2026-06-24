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

/** POST /api/v1/security/invitations → 201 | 404 (rol) | 409 (pending duplicado).
 *  SuperAdmin puede pasar targetTenantId para invitar a otro tenant. */
export async function createInvitation(
  email: string,
  fullName: string,
  roleId?: string,
  targetTenantId?: string,
): Promise<InvitationCreatedResult> {
  try {
    return await apiFetch<InvitationCreatedResult>("/api/v1/security/invitations", {
      method: "POST",
      body: {
        email,
        fullName,
        roleId: roleId || undefined,
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

/** GET /api/v1/security/modules → módulos accesibles según permisos del caller. */
export async function getAccessibleModules(): Promise<AccessibleModule[]> {
  return apiFetch<AccessibleModule[]>("/api/v1/security/modules");
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

/** POST /api/v1/security/users/{userId}/suspend — bloquea al usuario temporalmente. */
export async function blockUser(
  userId: string,
  reason: string,
  endsAt: string,
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
