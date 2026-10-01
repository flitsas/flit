// HU #13200 (AC6) — La pestaña «Clientes de integración» (clientes externos, p. ej. Flito) es exclusiva de
// SuperAdmin: un administrador de compañía no la ve, ni siquiera con el permiso de clientes ICT.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";
import { fetchExternalClients } from "@/lib/api/external-clients";

const perms = vi.hoisted(() => ({
  isSuperAdmin: true,
  isAdminCompany: false,
  isOtAdmin: false,
  permissions: [] as string[],
  tenantId: "tenant-1",
  userId: "user-1",
  roleId: "role-1",
  roleCode: "SuperAdmin",
}));

vi.mock("@/hooks/usePermissions", () => ({ usePermissions: () => perms }));

vi.mock("@/lib/api/security", () => ({
  getUsers: vi.fn().mockResolvedValue([]),
  getRoles: vi.fn().mockResolvedValue([]),
  createInvitation: vi.fn(),
  assignRole: vi.fn(),
  blockUser: vi.fn(),
  unblockUser: vi.fn(),
  updateUser: vi.fn(),
  deleteUser: vi.fn(),
  restoreUser: vi.fn(),
  resendInvitation: vi.fn(),
  cancelInvitation: vi.fn(),
  reactivateInvitation: vi.fn(),
}));

vi.mock("@/lib/api/admin-companies", () => ({ fetchAllCompanies: vi.fn().mockResolvedValue([]) }));
vi.mock("@/lib/api/admin-transit-office-tenants", () => ({ fetchAllTransitOfficeTenants: vi.fn().mockResolvedValue([]) }));
vi.mock("@/lib/api/ict-clients", () => ({ fetchIctClients: vi.fn().mockResolvedValue({ items: [] }) }));
vi.mock("@/lib/api/external-clients", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/external-clients")>()),
  fetchExternalClients: vi.fn().mockResolvedValue([]),
}));

describe("Usuarios — pestaña Clientes de integración (#13200)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    Object.assign(perms, { isSuperAdmin: true, isAdminCompany: false, permissions: [], roleCode: "SuperAdmin" });
  });

  it("AC6: SuperAdmin ve la pestaña y al abrirla se carga el submódulo", async () => {
    const ue = userEvent.setup();
    render(<Usuarios />);

    await ue.click(await screen.findByRole("button", { name: /^clientes de integración$/i }));

    expect(await screen.findByText(/no hay clientes de integración/i)).toBeInTheDocument();
    expect(fetchExternalClients).toHaveBeenCalledTimes(1);
  });

  it("AC6: un administrador de compañía no ve la pestaña, ni con el permiso de clientes ICT", async () => {
    Object.assign(perms, {
      isSuperAdmin: false,
      isAdminCompany: true,
      permissions: ["ict.clients.manage"],
      roleCode: "AdminCompany",
    });
    render(<Usuarios />);

    expect(await screen.findByRole("button", { name: /^clientes ict$/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^clientes de integración$/i })).not.toBeInTheDocument();
    expect(fetchExternalClients).not.toHaveBeenCalled();
  });
});
