import { apiFetch } from "./client";
import { ApiError } from "./types";

export interface InvitationCreatedResult {
  invitationId: string;
  email: string;
  emailSent: boolean;
}

/** POST /api/v1/security/invitations → 201 | 404 (rol) | 409 (pending duplicado). */
export async function createInvitation(
  email: string,
  roleId?: string,
): Promise<InvitationCreatedResult> {
  try {
    return await apiFetch<InvitationCreatedResult>("/api/v1/security/invitations", {
      method: "POST",
      body: { email, ...(roleId ? { roleId } : {}) },
    });
  } catch (err) {
    if (err instanceof ApiError) throw err;
    throw err;
  }
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
