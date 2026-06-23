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
  roleId: string,
): Promise<InvitationCreatedResult> {
  try {
    return await apiFetch<InvitationCreatedResult>("/api/v1/security/invitations", {
      method: "POST",
      body: { email, roleId },
    });
  } catch (err) {
    if (err instanceof ApiError) throw err;
    throw err;
  }
}
