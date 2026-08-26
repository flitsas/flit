// Refactor adminOT — el diálogo "Invitar usuario" del SuperAdmin (módulo Shell "Usuarios y
// Permisos") ya no crea siempre un AdminCompany: el rol se resuelve según el tipo de tenant
// destino, elegido entre compañías y organismos de tránsito en un mismo selector.
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";
import { getUsers } from "@/lib/api/security";

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

    // El destino es un combobox con buscador: las opciones solo existen con la lista abierta.
    const tenantSelect = await screen.findByLabelText(/compañía destino/i);
    await user.click(tenantSelect);
    await waitFor(() => {
      expect(screen.getByRole("option", { name: /Compañía Demo/i })).toBeInTheDocument();
    });
    expect(
      screen.queryByRole("option", { name: /Secretaría de Movilidad Demo/i }),
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole("option", { name: /Compañía Demo/i }));
    expect(tenantSelect).toHaveValue("Compañía Demo");
  });

  it("pide organismo de tránsito destino para el perfil OT", async () => {
    const user = userEvent.setup();
    render(<Usuarios />);

    await user.click(await screen.findByRole("button", { name: /invitar usuario/i }));
    await user.click(await screen.findByRole("radio", { name: /Organismo de Tránsito/i }));

    await user.click(await screen.findByLabelText(/organismo de tránsito destino/i));
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

// AC4 (HU #11551) — el módulo Usuarios es una de las tres pantallas que comparten UsersTable:
// las columnas Perfil y Rol deben quedar separadas y las acciones no se pierden.
describe("Usuarios — tabla con columnas Perfil y Rol separadas (AC4)", () => {
  it("muestra Perfil y Rol en columnas distintas y conserva las acciones", async () => {
    vi.mocked(getUsers).mockResolvedValueOnce([
      {
        id: "u-modulo-1",
        fullName: "Gina Paredes",
        email: "gina@flit.local",
        role: "Administrador de Compañía",
        roleCode: "AdminCompany",
        roleId: "role-admin-company",
        status: "active",
        createdAt: "2026-08-01T10:00:00Z",
        isSuspended: false,
        tenantType: "COMPANY",
        profile: "GESTOR",
        rowVersion: 1,
      },
    ]);

    render(<Usuarios />);

    const fila = (await screen.findByText("Gina Paredes")).closest("div.grid") as HTMLElement;
    const encabezado = screen.getByText("Usuario").closest("div.grid") as HTMLElement;
    expect(within(encabezado).getByText("Perfil")).toBeInTheDocument();
    expect(within(encabezado).getByText("Rol")).toBeInTheDocument();
    expect(within(fila).getByText("Gestor")).toBeInTheDocument();
    expect(within(fila).getByText("Administrador de Compañía")).toBeInTheDocument();
    expect(
      within(fila).getByRole("button", { name: /editar usuario gina paredes/i }),
    ).toBeInTheDocument();
  });
});
