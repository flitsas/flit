import { describe, expect, it } from "vitest";

import { resolveDrFlitContext, roleFromPayload } from "../dr-flit-context";

const TENANT = "0b6f0c9e-2a6e-4a3e-9d21-5c3c1f2a7b10";
const CHILD = "7d1e2f3a-4b5c-4d6e-8f90-a1b2c3d4e5f6";

describe("dr-flit-context", () => {
  describe("roleFromPayload — precedencia", () => {
    it("sin payload es gestor", () => {
      expect(roleFromPayload(null)).toBe("gestor");
    });

    it("SuperAdmin gana sobre cualquier otro rol", () => {
      expect(
        roleFromPayload({
          roles: [{ code: "AdminCompany" }, { code: "SuperAdmin" }, { code: "ot_admin" }],
        }),
      ).toBe("superadmin");
    });

    it("ot_admin gana sobre AdminCompany", () => {
      expect(roleFromPayload({ roles: [{ code: "AdminCompany" }, { code: "ot_admin" }] })).toBe(
        "ot_admin",
      );
    });

    it("AdminCompany por role_code (claim singular)", () => {
      expect(roleFromPayload({ role_code: "adminCompany" })).toBe("admin_company");
    });

    it("Radicador es gestor", () => {
      expect(roleFromPayload({ role: "Radicador", roles: [{ code: "Radicador" }] })).toBe("gestor");
    });
  });

  describe("resolveDrFlitContext — tenant y red", () => {
    it("gestor: tenant del JWT, sin red", () => {
      const ctx = resolveDrFlitContext({ role: "Radicador", tenant_id: TENANT });
      expect(ctx).toEqual({ role: "gestor", tenantId: TENANT, network: { active: false } });
    });

    it("tenant_id vacío se normaliza a null", () => {
      expect(resolveDrFlitContext({ tenant_id: "" }).tenantId).toBeNull();
    });

    it("AdminCompany con red activa (toda la red)", () => {
      const ctx = resolveDrFlitContext(
        { role_code: "AdminCompany", tenant_id: TENANT, is_group_parent: true },
        { networkActive: true, scope: { mode: "network" } },
      );
      expect(ctx.role).toBe("admin_company");
      expect(ctx.network).toEqual({ active: true });
    });

    it("AdminCompany con red activa sobre un hijo concreto", () => {
      const ctx = resolveDrFlitContext(
        { role_code: "AdminCompany", tenant_id: TENANT, is_group_parent: true },
        { networkActive: true, scope: { mode: "network", childTenantId: CHILD } },
      );
      expect(ctx.network).toEqual({ active: true, childTenantId: CHILD });
    });

    it("AdminCompany con alcance propio no activa red", () => {
      const ctx = resolveDrFlitContext(
        { role_code: "AdminCompany", tenant_id: TENANT, is_group_parent: true },
        { networkActive: false, scope: { mode: "own" } },
      );
      expect(ctx.network).toEqual({ active: false });
    });

    it("HU #12652 — Radicador de la cabeza nunca recibe red aunque el hook la marque activa", () => {
      const ctx = resolveDrFlitContext(
        { role: "Radicador", tenant_id: TENANT, is_group_parent: true },
        { networkActive: true, scope: { mode: "network" } },
      );
      expect(ctx.role).toBe("gestor");
      expect(ctx.network).toEqual({ active: false });
    });

    it("SuperAdmin nunca recibe red (ve todo por rol)", () => {
      const ctx = resolveDrFlitContext(
        { role: "SuperAdmin", tenant_id: TENANT },
        { networkActive: true, scope: { mode: "network" } },
      );
      expect(ctx.role).toBe("superadmin");
      expect(ctx.network).toEqual({ active: false });
    });

    it("sin input de red el alcance es propio", () => {
      const ctx = resolveDrFlitContext({ role_code: "AdminCompany", tenant_id: TENANT }, null);
      expect(ctx.network).toEqual({ active: false });
    });
  });
});
