import { describe, expect, it } from "vitest";
import {
  defaultChildTenantType,
  isHeadTenantType,
  selectableTenantTypes,
  tenantTypeLabel,
} from "../types";

describe("tenant type helpers (HU #12357 / #12356)", () => {
  it("etiqueta Concesionario de vehículos en UI", () => {
    expect(tenantTypeLabel("CONCESIONARIO")).toBe("Concesionario de vehículos");
  });

  it("detecta tipos de cabeza de grupo", () => {
    expect(isHeadTenantType("CONCESION")).toBe(true);
    expect(isHeadTenantType("MARCA_BLANCA")).toBe(true);
    expect(isHeadTenantType("RENTING")).toBe(false);
  });

  it("SuperAdmin recibe cinco tipos; otros tres", () => {
    expect(selectableTenantTypes(true)).toHaveLength(5);
    expect(selectableTenantTypes(false)).toHaveLength(3);
    expect(selectableTenantTypes(false)).not.toContain("CONCESION");
  });

  it("defaultChildTenantType según cabeza", () => {
    expect(defaultChildTenantType("CONCESION")).toBe("CONCESIONARIO");
    expect(defaultChildTenantType("MARCA_BLANCA")).toBe("RENTING");
  });
});
