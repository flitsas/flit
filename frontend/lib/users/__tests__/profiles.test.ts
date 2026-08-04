import { describe, expect, it } from "vitest";
import {
  inferProfile,
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
