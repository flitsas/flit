// Pestaña "Usuarios" de la ficha de compañía: tercera pantalla que comparte UsersTable con el
// módulo Usuarios y el hub OT (HU #11551 — AC4: columnas Perfil y Rol separadas en las tres).
import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { CompanyUsersPanel } from "../CompanyUsersPanel";
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
  assignRole: vi.fn(),
  updateUser: vi.fn(),
}));

vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: {
    listRoles: vi.fn().mockResolvedValue([]),
  },
}));

describe("CompanyUsersPanel — tabla con columnas Perfil y Rol separadas (AC4)", () => {
  it("muestra Perfil y Rol en columnas distintas y conserva las acciones", async () => {
    vi.mocked(getUsers).mockResolvedValueOnce([
      {
        id: "u-company-1",
        fullName: "Hugo Vélez",
        email: "hugo@flit.local",
        role: "Administrador de Compañía",
        roleCode: "AdminCompany",
        roleId: "role-admin-company",
        status: "active",
        createdAt: "2026-08-01T10:00:00Z",
        isSuspended: false,
        tenantId: "tenant-1",
        tenantType: "COMPANY",
        profile: "GESTOR",
        rowVersion: 1,
      },
    ]);

    render(<CompanyUsersPanel tenantId="tenant-1" />);

    const fila = (await screen.findByText("Hugo Vélez")).closest("div.grid") as HTMLElement;
    const encabezado = screen.getByText("Usuario").closest("div.grid") as HTMLElement;
    expect(within(encabezado).getByText("Perfil")).toBeInTheDocument();
    expect(within(encabezado).getByText("Rol")).toBeInTheDocument();
    expect(within(fila).getByText("Gestor")).toBeInTheDocument();
    expect(within(fila).getByText("Administrador de Compañía")).toBeInTheDocument();
    expect(
      within(fila).getByRole("button", { name: /editar usuario hugo vélez/i }),
    ).toBeInTheDocument();
  });
});
