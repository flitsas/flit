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
  resendInvitation: vi.fn(),
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

// El alta de usuario dejó de tener un único selector "empresa u organismo destino" con el rol
// forzado por el backend: ahora se elige primero el PERFIL (context/usuarios-contex.md) y el
// destino que se pide depende de él — el perfil FLIT no lleva compañía ni organismo.
describe("Usuarios — invitar usuario (SuperAdmin, selector de perfil)", () => {
  it("pide compañía destino para el perfil Gestor", async () => {
    const user = userEvent.setup();
    render(<Usuarios />);

    await user.click(await screen.findByRole("button", { name: /invitar usuario/i }));
    await user.click(await screen.findByRole("radio", { name: /Gestor/i }));

    const tenantSelect = await screen.findByLabelText(/compañía destino/i);
    await waitFor(() => {
      expect(screen.getByRole("option", { name: /Compañía Demo/i })).toBeInTheDocument();
    });
    expect(
      screen.queryByRole("option", { name: /Secretaría de Movilidad Demo/i }),
    ).not.toBeInTheDocument();

    await user.selectOptions(tenantSelect, "company-1");
    expect((tenantSelect as HTMLSelectElement).value).toBe("company-1");
  });

  it("pide organismo de tránsito destino para el perfil OT", async () => {
    const user = userEvent.setup();
    render(<Usuarios />);

    await user.click(await screen.findByRole("button", { name: /invitar usuario/i }));
    await user.click(await screen.findByRole("radio", { name: /Organismo de Tránsito/i }));

    await screen.findByLabelText(/organismo de tránsito destino/i);
    await waitFor(() => {
      expect(
        screen.getByRole("option", { name: /Secretaría de Movilidad Demo/i }),
      ).toBeInTheDocument();
    });
    expect(screen.queryByRole("option", { name: /Compañía Demo/i })).not.toBeInTheDocument();
  });

  it("no exige compañía ni organismo para el perfil FLIT", async () => {
    const user = userEvent.setup();
    render(<Usuarios />);

    await user.click(await screen.findByRole("button", { name: /invitar usuario/i }));
    await user.click(await screen.findByRole("radio", { name: /^FLIT/i }));

    expect(screen.queryByLabelText(/compañía destino/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/organismo de tránsito destino/i)).not.toBeInTheDocument();
    expect(await screen.findByText(/se creará con el rol de sistema/i)).toBeInTheDocument();
  });
});
