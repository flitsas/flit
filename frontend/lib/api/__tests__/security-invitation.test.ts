// HU #10510 — createInvitation ahora envía `roleIds: string[]` (mínimo 1 elemento para
// AdminCompany/OtAdmin) en vez del `roleId?: string` singular de HU #10175/#10506, alineado con
// `CreateInvitationRequest(string Email, string? FullName, Guid[]? RoleIds, Guid? TargetTenantId)`
// en SecurityEndpoints.cs.
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

const mocks = vi.hoisted(() => ({ getToken: vi.fn(() => "jwt-admin-company") }));
vi.mock("../client", async () => {
  const actual = await vi.importActual<typeof import("../client")>("../client");
  return { ...actual, getToken: mocks.getToken };
});

import { createInvitation } from "../security";
import { ApiError } from "../types";

const originalFetch = global.fetch;

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getToken.mockReturnValue("jwt-admin-company");
});

afterEach(() => {
  global.fetch = originalFetch;
});

describe("createInvitation (HU #10510)", () => {
  it("envía el body con roleIds: string[] (no roleId singular)", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(jsonResponse({ invitationId: "inv-1", email: "a@b.com", emailSent: true }, 201));
    global.fetch = fetchMock as never;

    await createInvitation("a@b.com", "A B", ["role-1", "role-2"]);

    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain("/api/v1/security/invitations");
    const body = JSON.parse(init.body as string);
    expect(body).toEqual({
      email: "a@b.com",
      fullName: "A B",
      roleIds: ["role-1", "role-2"],
      targetTenantId: undefined,
    });
    expect(body.roleId).toBeUndefined();
  });

  it("propaga el error 400 NO_ROLES_SELECTED del backend si se envía roleIds vacío", async () => {
    global.fetch = vi
      .fn()
      .mockImplementation(() =>
        Promise.resolve(
          jsonResponse({ code: "NO_ROLES_SELECTED", message: "Debes seleccionar al menos un rol para invitar al usuario." }, 400),
        ),
      ) as never;

    try {
      await createInvitation("a@b.com", "A B", []);
      expect.fail("debía lanzar");
    } catch (err) {
      expect(err).toBeInstanceOf(ApiError);
      expect((err as ApiError).status).toBe(400);
      expect((err as ApiError).body).toMatchObject({ code: "NO_ROLES_SELECTED" });
    }
  });
});
