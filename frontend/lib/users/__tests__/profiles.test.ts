import { describe, expect, it } from "vitest";
import {
  inferProfile,
  isSuperAdminRole,
  selectableRolesForProfile,
  profileShortLabel,
  resolveProfile,
  targetEntityTypeForProfile,
} from "../profiles";

describe("resolveProfile", () => {
  it("usa el perfil que calculó el backend", () => {
    expect(resolveProfile({ profile: "FLIT" })).toBe("FLIT");
    expect(resolveProfile({ profile: "OT" })).toBe("OT");
    expect(resolveProfile({ profile: "GESTOR" })).toBe("GESTOR");
  });

  it("acepta el perfil del backend sin importar mayúsculas", () => {
    expect(resolveProfile({ profile: "flit" })).toBe("FLIT");
  });

  // El caso que rompía antes: un rol personalizado dentro de un tenant OT se etiquetaba
  // "Gestor" porque el perfil se inferìa siempre del roleCode.
  it("respeta el perfil del backend aunque el rol sea personalizado", () => {
    expect(resolveProfile({ profile: "OT", roleCode: "revisor_documental" })).toBe("OT");
  });

  it("infiere solo cuando el backend no informó el perfil", () => {
    expect(resolveProfile({ roleCode: "SuperAdmin" })).toBe("FLIT");
    expect(resolveProfile({ roleCode: "ot_admin" })).toBe("OT");
    expect(resolveProfile({ roleCode: "custom", tenantType: "TRANSIT_OFFICE" })).toBe("OT");
    expect(resolveProfile({ roleCode: "custom", tenantType: "COMPANY" })).toBe("GESTOR");
    expect(resolveProfile({})).toBe("GESTOR");
  });

  it("ignora un perfil desconocido y cae al respaldo", () => {
    expect(resolveProfile({ profile: "OTRO", roleCode: "ot_admin" })).toBe("OT");
  });
});

describe("inferProfile", () => {
  it("prioriza SuperAdmin sobre el tipo de tenant", () => {
    expect(inferProfile("SuperAdmin", "TRANSIT_OFFICE")).toBe("FLIT");
  });
});

describe("targetEntityTypeForProfile", () => {
  it("mapea cada perfil al target_entity_type de sus roles", () => {
    expect(targetEntityTypeForProfile("OT")).toBe("TRANSIT_OFFICE");
    expect(targetEntityTypeForProfile("GESTOR")).toBe("COMPANY");
    // FLIT no tiene target_entity_type propio: el rol SuperAdmin vive con COMPANY.
    expect(targetEntityTypeForProfile("FLIT")).toBe("COMPANY");
  });
});

describe("profileShortLabel", () => {
  it("acorta el label de organismo de tránsito", () => {
    expect(profileShortLabel("OT")).toBe("OT");
    expect(profileShortLabel("GESTOR")).toBe("Gestor");
  });
});

describe("selectableRolesForProfile", () => {
  // El catálogo COMPANY incluye el rol SuperAdmin porque no tiene un target_entity_type propio;
  // sin filtro aparecía como opción al editar a un Gestor.
  const catalogo = [
    { id: "r1", code: "SuperAdmin" },
    { id: "r2", code: "AdminCompany" },
    { id: "r3", code: "Radicador" },
  ];

  it("nunca ofrece SuperAdmin a un Gestor ni a un usuario de OT", () => {
    expect(selectableRolesForProfile(catalogo, "GESTOR").map((r) => r.code)).toEqual([
      "AdminCompany",
      "Radicador",
    ]);
    expect(selectableRolesForProfile(catalogo, "OT").map((r) => r.code)).toEqual([
      "AdminCompany",
      "Radicador",
    ]);
  });

  it("en el perfil FLIT deja únicamente el rol SuperAdmin", () => {
    expect(selectableRolesForProfile(catalogo, "FLIT").map((r) => r.code)).toEqual(["SuperAdmin"]);
  });

  it("reconoce el código sin importar mayúsculas ni espacios", () => {
    expect(isSuperAdminRole(" superadmin ")).toBe(true);
    expect(isSuperAdminRole("SUPERADMIN")).toBe(true);
    expect(isSuperAdminRole("AdminCompany")).toBe(false);
    expect(isSuperAdminRole(null)).toBe(false);
  });
});
