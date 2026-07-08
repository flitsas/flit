// Refactor adminOT — el diálogo "Invitar usuario" del SuperAdmin (módulo Shell "Usuarios y
// Permisos") ya no crea siempre un AdminCompany: el rol se resuelve según el tipo de tenant
// destino, elegido entre compañías y organismos de tránsito en un mismo selector.
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: true,
    isAdminCompany: false,
    isOtAdmin: false,
    permissions: [],
    tenantId: "superadmin-tenant",
    userId: "superadmin-user",
    roleId: "role-superadmin",
    roleCode: "SuperAdmin",
  }),
}));

vi.mock("@/lib/api/security", () => ({
  getUsers: vi.fn().mockResolvedValue([]),
  getRoles: vi.fn().mockResolvedValue([]),
  createInvitation: vi.fn().mockResolvedValue({ email: "nuevo@flit.local", emailSent: true }),
  assignRole: vi.fn(),
  blockUser: vi.fn(),
  unblockUser: vi.fn(),
  updateUser: vi.fn(),
  deleteUser: vi.fn(),
  restoreUser: vi.fn(),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: vi.fn().mockResolvedValue({
    data: [{ id: "company-1", razonSocial: "Compañía Demo", nit: "900111111-1" }],
    totalCount: 1,
    page: 1,
    pageSize: 200,
  }),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficeTenants: vi.fn().mockResolvedValue({
    data: [
      {
        id: "ot-tenant-1",
        legalName: "Secretaría de Movilidad Demo",
        taxId: "900222222-2",
        code: "OT-DEMO",
        tenantType: "RENTING",
        estadoActivo: true,
        fechaCreacion: "2026-07-01T00:00:00Z",
        rowVersion: 0,
        transitOfficeId: "office-1",
        transitOfficeName: "Secretaría de Movilidad Demo",
        transitOfficeCode: "11001",
        operationMode: "dashboard",
      },
    ],
    totalCount: 1,
    page: 1,
    pageSize: 200,
  }),
}));

describe("Usuarios — invitar usuario (SuperAdmin, selector compañía/OT)", () => {
  it("muestra compañías y organismos de tránsito agrupados, y resuelve el rol según el destino elegido", async () => {
    const user = userEvent.setup();
    render(<Usuarios />);

    await user.click(await screen.findByRole("button", { name: /invitar usuario/i }));

    const tenantSelect = await screen.findByLabelText(/empresa u organismo destino/i);
    await waitFor(() => {
      expect(screen.getByRole("option", { name: /Compañía Demo/i })).toBeInTheDocument();
      expect(screen.getByRole("option", { name: /Secretaría de Movilidad Demo/i })).toBeInTheDocument();
    });

    await user.selectOptions(tenantSelect, "company-1");
    expect(await screen.findByText(/Administrador de Compañía/)).toBeInTheDocument();

    await user.selectOptions(tenantSelect, "ot-tenant-1");
    expect(await screen.findByText(/Administrador OT/)).toBeInTheDocument();
    expect(screen.queryByText(/Administrador de Compañía/)).not.toBeInTheDocument();
  });
});
