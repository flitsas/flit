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
  status: "active" | "inactive" | "pending";
  createdAt: string | null;
}

/** POST /api/v1/security/invitations → 201 | 404 (rol) | 409 (pending duplicado). */
export async function createInvitation(
  email: string,
  fullName: string,
  roleId?: string,
): Promise<InvitationCreatedResult> {
  try {
    return await apiFetch<InvitationCreatedResult>("/api/v1/security/invitations", {
      method: "POST",
      body: { email, fullName, roleId: roleId || undefined },
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
