// HU #10509 — cliente de roles del catálogo GLOBAL (HU #10505/#10508):
// - Ya no envía el header X-Flit-SuperAdmin (obsoleto, el backend exige JWT SuperAdmin real).
// - listRoles/createRole/setRolePermissions usan targetEntityType en vez de tenantId.
// - activateRole/deactivateRole llaman a los nuevos PATCH .../activate y .../deactivate.
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

const mocks = vi.hoisted(() => ({ getToken: vi.fn(() => "jwt-superadmin") }));
vi.mock("../client", () => ({ getToken: mocks.getToken }));

import { superadminClient } from "../superadmin-client";

const originalFetch = global.fetch;

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getToken.mockReturnValue("jwt-superadmin");
});

afterEach(() => {
  global.fetch = originalFetch;
});

describe("superadminClient — roles (HU #10509)", () => {
  it("listRoles envía targetEntityType como query param y Authorization Bearer, sin X-Flit-SuperAdmin", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]));
    global.fetch = fetchMock as never;

    await superadminClient.listRoles("COMPANY");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain("/api/v1/superadmin/roles?targetEntityType=COMPANY");
    const headers = init.headers as Record<string, string>;
    expect(headers.Authorization).toBe("Bearer jwt-superadmin");
    expect(headers["X-Flit-SuperAdmin"]).toBeUndefined();
  });

  it("createRole envía targetEntityType en el body (no tenantId)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: "role-1" }, 201));
    global.fetch = fetchMock as never;

    const result = await superadminClient.createRole({
      targetEntityType: "TRANSIT_OFFICE",
      code: "OT_SUPERVISOR",
      name: "Supervisor OT",
    });

    expect(result).toEqual({ id: "role-1" });
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain("/api/v1/superadmin/roles");
    const body = JSON.parse(init.body as string);
    expect(body).toEqual({ targetEntityType: "TRANSIT_OFFICE", code: "OT_SUPERVISOR", name: "Supervisor OT" });
  });

  it("setRolePermissions ya no envía tenantId y devuelve el RoleDetail completo", async () => {
    const detail = {
      id: "role-1",
      targetEntityType: "COMPANY",
      code: "SUPERVISOR",
      name: "Supervisor",
      description: null,
      isSystem: false,
      isActive: true,
      permissions: [{ id: "perm-1", slug: "tramites.read", name: "Leer trámites" }],
    };
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(detail));
    global.fetch = fetchMock as never;

    const result = await superadminClient.setRolePermissions("role-1", ["perm-1"]);

    expect(result).toEqual(detail);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain("/api/v1/superadmin/roles/role-1/permissions");
    expect(init.method).toBe("PUT");
    expect(JSON.parse(init.body as string)).toEqual({ permissionIds: ["perm-1"] });
  });

  it("activateRole y deactivateRole llaman a los endpoints PATCH correctos (HU #10508)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 200 }));
    global.fetch = fetchMock as never;

    await superadminClient.activateRole("role-1");
    expect(String(fetchMock.mock.calls[0][0])).toContain("/api/v1/superadmin/roles/role-1/activate");
    expect(fetchMock.mock.calls[0][1].method).toBe("PATCH");

    await superadminClient.deactivateRole("role-1");
    expect(String(fetchMock.mock.calls[1][0])).toContain("/api/v1/superadmin/roles/role-1/deactivate");
    expect(fetchMock.mock.calls[1][1].method).toBe("PATCH");
  });

  it("deleteRole propaga el mensaje 409 (AC3) para que la UI distinga ROLE_HAS_ACTIVE_USERS", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(jsonResponse({ code: "ROLE_HAS_ACTIVE_USERS" }, 409)) as never;

    await expect(superadminClient.deleteRole("role-1")).rejects.toThrow(/ROLE_HAS_ACTIVE_USERS/);
  });
});
