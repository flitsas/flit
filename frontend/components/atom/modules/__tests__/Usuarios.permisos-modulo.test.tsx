// HU #13652 — acción «Permisos por módulo» en la tabla de Usuarios del Panel SA (solo Super Admin).
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";
import { getUsers } from "@/lib/api/security";
import type { TenantUser } from "@/lib/api/security";
import { ADMIN_READ_ONLY_MESSAGE } from "@/lib/permissions/modulePermissions";
import { listGrantablePermissions, listPermissionAudit } from "@/lib/api/permission-overrides";

const perms = vi.hoisted(() => ({ isSuperAdmin: true, isAdminCompany: false }));

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: perms.isSuperAdmin,
    isAdminCompany: perms.isAdminCompany,
    isOtAdmin: false,
    permissions: [],
    tenantId: "tenant-1",
    userId: "user-1",
    roleId: "role-1",
    roleCode: "AdminCompany",
  }),
}));

vi.mock("@/lib/api/security", () => ({
  getUsers: vi.fn(),
  getRoles: vi.fn().mockResolvedValue([]),
  createInvitation: vi.fn(),
  assignRole: vi.fn(),
  blockUser: vi.fn(),
  unblockUser: vi.fn(),
  updateUser: vi.fn(),
  deleteUser: vi.fn(),
  restoreUser: vi.fn(),
  resendInvitation: vi.fn(),
  getAccessibleModules: vi.fn().mockResolvedValue([]),
  getTenantRole: vi.fn(),
}));
vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: { getRole: vi.fn().mockResolvedValue({ permissions: [] }), listRoles: vi.fn().mockResolvedValue([]) },
}));
vi.mock("@/lib/api/permission-overrides", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/permission-overrides")>("@/lib/api/permission-overrides");
  return {
    ...actual,
    listGrantablePermissions: vi.fn().mockResolvedValue([]),
    listUserOverrides: vi.fn().mockResolvedValue([]),
    listUserRolePermissionIds: vi.fn().mockResolvedValue({ permissionIds: [] }),
    listPermissionAudit: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 10 }),
  };
});
vi.mock("@/lib/api/admin-companies", () => ({ fetchAllCompanies: vi.fn().mockResolvedValue([]) }));
vi.mock("@/lib/api/admin-transit-office-tenants", () => ({ fetchAllTransitOfficeTenants: vi.fn().mockResolvedValue([]) }));

const base: TenantUser = {
  id: "u-1", fullName: "Ana Torres", email: "ana@flit.local", role: "Supervisor", roleCode: "SUPERVISOR",
  roleId: "r-1", status: "active", createdAt: "2026-06-01T00:00:00Z", isSuspended: false, rowVersion: 1, tenantId: "t-1",
};
const superAdminUser: TenantUser = { ...base, id: "u-sa", fullName: "Root FLIT", email: "root@flit.local", roleCode: "superadmin", roleId: "r-sa" };
const pending: TenantUser = { ...base, id: "inv-1", fullName: "Invitado", email: "inv@flit.local", status: "pending", roleId: null };

beforeEach(() => {
  vi.clearAllMocks();
  perms.isSuperAdmin = true;
  perms.isAdminCompany = false;
});

describe("Usuarios — Permisos por módulo (#13652)", () => {
  it("el Super Admin abre la ficha de permisos del usuario", async () => {
    vi.mocked(getUsers).mockResolvedValue([base]);
    render(<Usuarios />);
    await screen.findByText("Ana Torres");
    await userEvent.click(screen.getByRole("button", { name: "Permisos por módulo de Ana Torres" }));
    expect(await screen.findByText("Permisos por módulo — Ana Torres")).toBeInTheDocument();
  });

  it("AC4: un usuario Super Admin muestra el aviso sin controles", async () => {
    vi.mocked(getUsers).mockResolvedValue([superAdminUser]);
    render(<Usuarios />);
    await screen.findByText("Root FLIT");
    await userEvent.click(screen.getByRole("button", { name: "Permisos por módulo de Root FLIT" }));
    expect(await screen.findByText("Los Super Admin no tienen permisos individuales.")).toBeInTheDocument();
  });

  it("AC5: una invitación pendiente muestra la sección deshabilitada", async () => {
    vi.mocked(getUsers).mockResolvedValue([pending]);
    render(<Usuarios />);
    await screen.findByText("Invitado");
    await userEvent.click(screen.getByRole("button", { name: "Permisos por módulo de Invitado" }));
    expect(await screen.findByText(/cuando el usuario acepte la invitación/)).toBeInTheDocument();
  });

  it("quien no es administrador no ve la acción", async () => {
    perms.isSuperAdmin = false;
    vi.mocked(getUsers).mockResolvedValue([base]);
    render(<Usuarios />);
    await screen.findByText("Ana Torres");
    expect(screen.queryByRole("button", { name: /Permisos por módulo de/ })).not.toBeInTheDocument();
  });
});

describe("Usuarios — pestaña Auditoría de permisos (#13654)", () => {
  it("el Super Admin ve la pestaña y la vista de auditoría", async () => {
    vi.mocked(getUsers).mockResolvedValue([base]);
    render(<Usuarios />);
    await screen.findByText("Ana Torres");
    await userEvent.click(screen.getByRole("button", { name: "Auditoría de permisos" }));
    expect(await screen.findByRole("region", { name: "Auditoría de permisos individuales" })).toBeInTheDocument();
  });

  it("quien no es administrador no ve la pestaña", async () => {
    perms.isSuperAdmin = false;
    vi.mocked(getUsers).mockResolvedValue([base]);
    render(<Usuarios />);
    await screen.findByText("Ana Torres");
    expect(screen.queryByRole("button", { name: "Auditoría de permisos" })).not.toBeInTheDocument();
  });
});

// HU #13655/#13656/#13658 (Feature #13104 F-D2) — Admin de Compañía en el módulo Usuarios (modo admin).
describe("Usuarios — Admin de Compañía (#13655, #13656, #13658)", () => {
  const admin: TenantUser = { ...base, id: "u-adm", fullName: "Beto Admin", email: "beto@flit.local", roleCode: "AdminCompany", roleId: "r-adm" };
  const self: TenantUser = { ...base, id: "user-1", fullName: "Yo Mismo", email: "yo@flit.local", roleCode: "AdminCompany", roleId: "r-adm" };

  beforeEach(() => {
    perms.isSuperAdmin = false;
    perms.isAdminCompany = true;
  });

  it("#13655 AC1: el Admin de Compañía abre «Permisos por módulo» de un usuario de su compañía, en modo admin", async () => {
    vi.mocked(getUsers).mockResolvedValue([base]);
    render(<Usuarios />);
    await screen.findByText("Ana Torres");
    await userEvent.click(screen.getByRole("button", { name: "Permisos por módulo de Ana Torres" }));
    expect(await screen.findByText("Permisos por módulo — Ana Torres")).toBeInTheDocument();
    // Modo admin: el tope sale de /grantable sin tenantId, y los usuarios sin rol admin quedan editables.
    expect(listGrantablePermissions).toHaveBeenCalledWith(undefined);
    expect(screen.queryByText(ADMIN_READ_ONLY_MESSAGE)).not.toBeInTheDocument();
  });

  it("#13656 AC1: la ficha de otro administrador queda en solo lectura con el mensaje", async () => {
    vi.mocked(getUsers).mockResolvedValue([admin]);
    render(<Usuarios />);
    await screen.findByText("Beto Admin");
    await userEvent.click(screen.getByRole("button", { name: "Permisos por módulo de Beto Admin" }));
    expect(await screen.findByText(ADMIN_READ_ONLY_MESSAGE)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Habilitar el módulo/ })).not.toBeInTheDocument();
  });

  it("#13656 AC2: la propia ficha queda en solo lectura con el mismo mensaje", async () => {
    vi.mocked(getUsers).mockResolvedValue([self]);
    render(<Usuarios />);
    await screen.findByText("Yo Mismo");
    await userEvent.click(screen.getByRole("button", { name: "Permisos por módulo de Yo Mismo" }));
    expect(await screen.findByText(ADMIN_READ_ONLY_MESSAGE)).toBeInTheDocument();
  });

  it("#13656 AC3: un usuario sin rol de administrador tiene los controles habilitados", async () => {
    vi.mocked(getUsers).mockResolvedValue([base]);
    render(<Usuarios />);
    await screen.findByText("Ana Torres");
    await userEvent.click(screen.getByRole("button", { name: "Permisos por módulo de Ana Torres" }));
    await screen.findByText("Permisos por módulo — Ana Torres");
    expect(screen.queryByText(ADMIN_READ_ONLY_MESSAGE)).not.toBeInTheDocument();
  });

  it("#13658: el Admin de Compañía ve la pestaña de auditoría, sin filtro de compañía, y la API sin tenantId", async () => {
    vi.mocked(getUsers).mockResolvedValue([base]);
    render(<Usuarios />);
    await screen.findByText("Ana Torres");
    await userEvent.click(screen.getByRole("button", { name: "Auditoría de permisos" }));
    expect(await screen.findByRole("region", { name: "Auditoría de permisos individuales" })).toBeInTheDocument();
    expect(screen.queryByRole("combobox", { name: "Compañía" })).not.toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Usuario" })).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Administrador" })).toBeInTheDocument();
    expect(listPermissionAudit).toHaveBeenCalledWith(expect.objectContaining({ tenantId: undefined }));
  });
});
