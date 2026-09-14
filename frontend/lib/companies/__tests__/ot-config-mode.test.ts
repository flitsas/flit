import { describe, expect, it } from "vitest";
import {
  otConfigPanelLegend,
  otConfigPanelReadOnly,
  resolveOtConfigPanelMode,
  showSuperAdminTransitBlocksPanel,
} from "@/lib/companies/ot-config-mode";
import type { CompanyListItem } from "@/lib/api/types";

const baseCompany = (overrides: Partial<CompanyListItem>): CompanyListItem => ({
  id: "t1",
  nit: "900",
  razonSocial: "Demo",
  code: "demo",
  tenantType: "RENTING",
  isTransitOffice: false,
  estadoActivo: true,
  fechaCreacion: "2026-01-01",
  rowVersion: 1,
  ...overrides,
});

describe("ot-config-mode (HU #12351 / #12408)", () => {
  it("marca hijo de Concesión como heredado en solo lectura", () => {
    const mode = resolveOtConfigPanelMode({
      company: baseCompany({ parentTenantId: "head", tenantType: "RENTING" }),
      parentTenantType: "CONCESION",
      isSuperAdmin: false,
      isAdminCompany: true,
    });
    expect(mode).toBe("readonly-concession-inherited");
    expect(otConfigPanelReadOnly(mode)).toBe(true);
    expect(otConfigPanelLegend(mode)).toMatch(/gobierna su Concesión/i);
  });

  it("SuperAdmin en Concesión puede editar con advertencia de alcance", () => {
    const mode = resolveOtConfigPanelMode({
      company: baseCompany({ tenantType: "CONCESION" }),
      parentTenantType: null,
      isSuperAdmin: true,
      isAdminCompany: false,
    });
    expect(mode).toBe("superadmin-concession");
    expect(otConfigPanelReadOnly(mode)).toBe(false);
  });

  it("solo SuperAdmin en Marca Blanca cabeza ve panel de bloqueos", () => {
    expect(
      showSuperAdminTransitBlocksPanel(baseCompany({ tenantType: "MARCA_BLANCA" }), true),
    ).toBe(true);
    expect(
      showSuperAdminTransitBlocksPanel(baseCompany({ tenantType: "CONCESION" }), true),
    ).toBe(false);
  });
});
