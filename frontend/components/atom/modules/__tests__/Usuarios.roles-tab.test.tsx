// HU #10509 AC4 + HU #13443 — la pestaña "Roles y permisos" de Usuarios.tsx: el SuperAdmin recibe un atajo al CRUD
// completo en RbacAdmin (?m=rbac); el Admin de Compañía gestiona sus roles propios (los globales de FLIT quedan en
// solo lectura); ot_admin y los demás roles no ven la pestaña.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";

const permissionsState = vi.hoisted(() => ({ isSuperAdmin: false, isAdminCompany: true, isOtAdmin: false }));

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: permissionsState.isSuperAdmin,
    isAdminCompany: permissionsState.isAdminCompany,
    isOtAdmin: permissionsState.isOtAdmin,
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
  tenantId: null as string | null,
}));
const ownRoleFixture = vi.hoisted(() => ({ ...roleFixture, id: "role-2", code: "contador", name: "Contador", tenantId: "tenant-1" }));

vi.mock("@/lib/api/security", () => ({
  getUsers: vi.fn().mockResolvedValue([]),
  getRoles: vi.fn().mockResolvedValue([roleFixture, ownRoleFixture]),
  getGrantablePermissions: vi.fn().mockResolvedValue([]),
  getTenantRole: vi.fn(),
  createTenantRole: vi.fn(),
  updateTenantRole: vi.fn(),
  setTenantRolePermissions: vi.fn(),
  deleteTenantRole: vi.fn(),
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
  fetchAllCompanies: vi.fn().mockResolvedValue([]),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchAllTransitOfficeTenants: vi.fn().mockResolvedValue([]),
}));

beforeEach(() => {
  vi.clearAllMocks();
});

describe("Usuarios — pestaña Roles y permisos", () => {
  beforeEach(() => {
    Object.assign(permissionsState, { isSuperAdmin: false, isAdminCompany: true, isOtAdmin: false });
  });

  it("HU #13443 AC1: el Admin de Compañía gestiona sus roles propios y ve los globales en solo lectura", async () => {
    const user = userEvent.setup();
    render(<Usuarios />);

    await user.click(await screen.findByRole("button", { name: /roles y permisos/i }));

    expect(await screen.findByText("Supervisor")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /nuevo rol/i })).toBeInTheDocument();
    // Rol propio: editar y eliminar.
    expect(screen.getByRole("button", { name: /editar contador/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /eliminar contador/i })).toBeInTheDocument();
    // Rol global de FLIT: solo ver permisos.
    expect(screen.getByRole("button", { name: /ver permisos de supervisor/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /editar supervisor/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /eliminar supervisor/i })).not.toBeInTheDocument();
  });

  it("HU #13443 AC3: ot_admin no ve la pestaña ni las acciones de gestión de roles", async () => {
    Object.assign(permissionsState, { isAdminCompany: false, isOtAdmin: true });
    render(<Usuarios />);

    await screen.findByRole("button", { name: /^usuarios$/i });
    expect(screen.queryByRole("button", { name: /roles y permisos/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /nuevo rol/i })).not.toBeInTheDocument();
  });

  it("SuperAdmin no ve la tabla de solo lectura: recibe un atajo al módulo RBAC (?m=rbac)", async () => {
    Object.assign(permissionsState, { isSuperAdmin: true, isAdminCompany: false });
    const user = userEvent.setup();
    render(<Usuarios />);

    await user.click(await screen.findByRole("button", { name: /roles y permisos/i }));

    const link = await screen.findByRole("link", { name: /ir a roles y permisos \(rbac\)/i });
    expect(link).toHaveAttribute("href", "/?m=rbac");
    expect(screen.queryByText("Supervisor")).not.toBeInTheDocument();
  });
});
