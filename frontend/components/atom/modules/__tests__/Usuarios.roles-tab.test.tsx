// HU #10509 AC4 — la pestaña "Roles y permisos" de Usuarios.tsx es de solo lectura para
// AdminCompany/OtAdmin (sin crear/editar/eliminar/desactivar) y, para el SuperAdmin, redirige
// al CRUD completo en RbacAdmin (?m=rbac) en vez de duplicar la lógica de gobernanza.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";

const permissionsState = vi.hoisted(() => ({ isSuperAdmin: false }));

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: permissionsState.isSuperAdmin,
    isAdminCompany: !permissionsState.isSuperAdmin,
    isOtAdmin: false,
    permissions: [],
    tenantId: "tenant-1",
    userId: "user-1",
    roleId: "role-1",
    roleCode: permissionsState.isSuperAdmin ? "SuperAdmin" : "AdminCompany",
  }),
}));

const roleFixture = vi.hoisted(() => ({
  id: "role-1",
  code: "SUPERVISOR",
  name: "Supervisor",
  description: "Rol de la empresa",
  isSystem: false,
  permissionCount: 3,
  createdAt: "2026-01-01T00:00:00Z",
}));

vi.mock("@/lib/api/security", () => ({
  getUsers: vi.fn().mockResolvedValue([]),
  getRoles: vi.fn().mockResolvedValue([roleFixture]),
  createInvitation: vi.fn(),
  assignRole: vi.fn(),
  blockUser: vi.fn(),
  unblockUser: vi.fn(),
  updateUser: vi.fn(),
  deleteUser: vi.fn(),
  restoreUser: vi.fn(),
  resendInvitation: vi.fn(),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficeTenants: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

beforeEach(() => {
  vi.clearAllMocks();
});

describe("Usuarios — pestaña Roles y permisos (AC4)", () => {
  it("AdminCompany ve la tabla de roles en modo SOLO LECTURA (sin botones de gestión)", async () => {
    permissionsState.isSuperAdmin = false;
    const user = userEvent.setup();
    render(<Usuarios />);

    await user.click(await screen.findByRole("button", { name: /roles y permisos/i }));

    expect(await screen.findByText("Supervisor")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /nuevo rol/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /editar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /eliminar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /desactivar/i })).not.toBeInTheDocument();
  });

  it("SuperAdmin no ve la tabla de solo lectura: recibe un atajo al módulo RBAC (?m=rbac)", async () => {
    permissionsState.isSuperAdmin = true;
    const user = userEvent.setup();
    render(<Usuarios />);

    await user.click(await screen.findByRole("button", { name: /roles y permisos/i }));

    const link = await screen.findByRole("link", { name: /ir a roles y permisos \(rbac\)/i });
    expect(link).toHaveAttribute("href", "/?m=rbac");
    expect(screen.queryByText("Supervisor")).not.toBeInTheDocument();
  });
});
