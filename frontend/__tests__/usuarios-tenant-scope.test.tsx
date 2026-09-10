/** HU #11230 — Usuarios: AdminCompany scoped vs SuperAdmin global. */
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { Usuarios } from "@/components/atom/modules/Usuarios";
import { getUsers } from "@/lib/api/security";
import type { TenantUser } from "@/lib/api/security";

const permissionsState = vi.hoisted(() => ({
  isSuperAdmin: false,
  isAdminCompany: true,
  isOtAdmin: false,
  tenantId: "tenant-ac-1" as string | null,
  permissions: [] as string[],
  userId: "user-1" as string | null,
  roleId: "role-1" as string | null,
  roleCode: "AdminCompany" as string | null,
}));

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => permissionsState,
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
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficeTenants: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

const tenantUser: TenantUser = {
  id: "u1",
  fullName: "Ana Tenant",
  email: "ana@acme.test",
  role: "Operador",
  roleCode: "Operador",
  roleId: "role-1",
  status: "active",
  createdAt: "2026-06-01T00:00:00Z",
  isSuspended: false,
  rowVersion: 1,
};

const globalUser: TenantUser = {
  id: "u2",
  fullName: "Luis Global",
  email: "luis@other.test",
  role: "AdminCompany",
  roleCode: "AdminCompany",
  roleId: "role-2",
  status: "active",
  createdAt: "2026-06-01T00:00:00Z",
  isSuspended: false,
  tenantId: "t-other",
  tenantName: "Otra SAS",
  rowVersion: 1,
};

describe("Usuarios HU #11230 — alcance por rol", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AdminCompany lista usuarios sin columna Empresa (solo su tenant)", async () => {
    permissionsState.isSuperAdmin = false;
    permissionsState.isAdminCompany = true;
    permissionsState.roleCode = "AdminCompany";
    vi.mocked(getUsers).mockResolvedValue([tenantUser]);

    render(<Usuarios />);

    expect(await screen.findByText("Ana Tenant")).toBeInTheDocument();
    expect(screen.queryByText("Empresa")).not.toBeInTheDocument();
  });

  it("SuperAdmin muestra columna Empresa (alcance global)", async () => {
    permissionsState.isSuperAdmin = true;
    permissionsState.isAdminCompany = false;
    permissionsState.roleCode = "SuperAdmin";
    vi.mocked(getUsers).mockResolvedValue([globalUser]);

    render(<Usuarios />);

    expect(await screen.findByText("Luis Global")).toBeInTheDocument();
    expect(screen.getByText("Empresa")).toBeInTheDocument();
    expect(screen.getByText("Otra SAS")).toBeInTheDocument();
  });
});
