// HU #10504 — [FRONTEND] Picker de grants de módulo por compañía/OT en acordeón con
// selección rápida ("Todos" / "Todas las compañías" / "Todas las OT").
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RbacAdmin } from "../RbacAdmin";

const { mod, company, otTenant } = vi.hoisted(() => ({
  mod: {
    id: "mod-1",
    code: "tramites",
    name: "Trámites",
    description: "Gestión de trámites",
    sortOrder: 1,
    isActive: true,
    permissionCount: 2,
    createdAt: "2026-01-01T00:00:00Z",
  },
  company: { id: "c1", nit: "900123456", razonSocial: "Empresa Demo S.A.S", estadoActivo: true },
  otTenant: {
    id: "t1",
    legalName: "Secretaría de Movilidad Bogotá OT",
    taxId: "800111222",
    code: "OT-01",
    tenantType: "RENTING" as const,
    estadoActivo: true,
    fechaCreacion: "2026-01-01T00:00:00Z",
    rowVersion: 1,
    transitOfficeId: "to-1",
    transitOfficeName: "Movilidad Bogotá",
    transitOfficeCode: "MOVBOG",
    operationMode: "dashboard" as const,
  },
}));

const grantModuleToTenant = vi.fn();
const revokeModuleFromTenant = vi.fn();
const listModuleGrants = vi.fn();

vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: {
    listModules: vi.fn().mockResolvedValue([mod]),
    listPermissions: vi.fn().mockResolvedValue([]),
    listCompanies: vi.fn().mockResolvedValue({ data: [company] }),
    listModuleGrants: (...args: unknown[]) => listModuleGrants(...args),
    grantModuleToTenant: (...args: unknown[]) => grantModuleToTenant(...args),
    revokeModuleFromTenant: (...args: unknown[]) => revokeModuleFromTenant(...args),
  },
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficeTenants: vi.fn().mockResolvedValue({ data: [otTenant], totalCount: 1, page: 1, pageSize: 20 }),
}));

beforeEach(() => {
  vi.clearAllMocks();
  listModuleGrants.mockResolvedValue([]);
  grantModuleToTenant.mockResolvedValue(undefined);
  revokeModuleFromTenant.mockResolvedValue(undefined);
});

async function openGrantsModal() {
  render(<RbacAdmin />);
  const btn = await screen.findByRole("button", { name: /gestionar empresas/i });
  await userEvent.click(btn);
  await screen.findByText("Empresa Demo S.A.S");
  await screen.findByText("Secretaría de Movilidad Bogotá OT");
}

describe("RbacAdmin — ModuleGrantsModal (HU #10504)", () => {
  it('"Todas las compañías" otorga solo a compañías, no a organismos de tránsito', async () => {
    const user = userEvent.setup();
    await openGrantsModal();

    await user.click(screen.getByRole("checkbox", { name: /todas las compañías/i }));

    await waitFor(() => expect(grantModuleToTenant).toHaveBeenCalledWith("mod-1", "c1"));
    expect(grantModuleToTenant).not.toHaveBeenCalledWith("mod-1", "t1");
  });

  it('"Todas las OT" otorga solo a organismos de tránsito, no a compañías', async () => {
    const user = userEvent.setup();
    await openGrantsModal();

    await user.click(screen.getByRole("checkbox", { name: /todas las ot/i }));

    await waitFor(() => expect(grantModuleToTenant).toHaveBeenCalledWith("mod-1", "t1"));
    expect(grantModuleToTenant).not.toHaveBeenCalledWith("mod-1", "c1");
  });

  it('"Todos" otorga a compañías y organismos de tránsito combinados', async () => {
    const user = userEvent.setup();
    await openGrantsModal();

    await user.click(screen.getByRole("checkbox", { name: /^todos$/i }));

    await waitFor(() => {
      expect(grantModuleToTenant).toHaveBeenCalledWith("mod-1", "c1");
      expect(grantModuleToTenant).toHaveBeenCalledWith("mod-1", "t1");
    });
  });

  it("permite marcar un tenant individual sin afectar a los demás", async () => {
    const user = userEvent.setup();
    await openGrantsModal();

    await user.click(screen.getByRole("checkbox", { name: /empresa demo s\.a\.s/i }));

    await waitFor(() => expect(grantModuleToTenant).toHaveBeenCalledWith("mod-1", "c1"));
    expect(grantModuleToTenant).not.toHaveBeenCalledWith("mod-1", "t1");
  });
});
