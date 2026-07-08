// HU #10623 — Botón "Eliminar" del menú de acciones (AdminCompany). AC1: confirmación clara
// (solo SuperAdmin restaura). AC2: sin auto-eliminación. AC4: sin pestaña "Eliminados".
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";
import { getUsers, deleteUser } from "@/lib/api/security";
import type { TenantUser } from "@/lib/api/security";

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: false,
    isAdminCompany: true,
    isOtAdmin: false,
    permissions: [],
    tenantId: "tenant-1",
    userId: "user-self",
    roleId: "role-admin",
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
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficeTenants: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

const otherUser: TenantUser = {
  id: "user-other",
  fullName: "Ana Torres",
  email: "ana@flit.local",
  role: "Supervisor",
  roleCode: "SUPERVISOR",
  roleId: "role-supervisor",
  status: "active",
  createdAt: "2026-06-01T00:00:00Z",
  isSuspended: false,
  rowVersion: 2,
};

const selfUser: TenantUser = {
  id: "user-self",
  fullName: "Yo Mismo",
  email: "yo@flit.local",
  role: "AdminCompany",
  roleCode: "ADMIN_COMPANY",
  roleId: "role-admin",
  status: "active",
  createdAt: "2026-06-01T00:00:00Z",
  isSuspended: false,
  rowVersion: 1,
};

describe("Usuarios — botón Eliminar (#10623, AdminCompany)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC2: no muestra el botón Eliminar sobre la propia fila del usuario autenticado", async () => {
    vi.mocked(getUsers).mockResolvedValue([selfUser, otherUser]);
    render(<Usuarios />);

    await screen.findByText("Ana Torres");
    expect(
      screen.queryByRole("button", { name: /eliminar usuario yo mismo/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /eliminar usuario ana torres/i }),
    ).toBeInTheDocument();
  });

  it("AC1: abre la confirmación con el aviso de que solo un Super Admin puede restaurar", async () => {
    vi.mocked(getUsers).mockResolvedValue([otherUser]);
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Ana Torres");
    await user.click(screen.getByRole("button", { name: /eliminar usuario ana torres/i }));

    // El texto está partido por un <strong>, se busca por el textContent del párrafo completo.
    expect(
      await screen.findByText(
        (_, node) => node?.tagName === "P" && /solo un.*super admin.*puede restaurarlo/i.test(node.textContent ?? ""),
      ),
    ).toBeInTheDocument();
  });

  it("confirma la eliminación llamando a deleteUser con el rowVersion leído y recarga el listado", async () => {
    vi.mocked(getUsers).mockResolvedValue([otherUser]);
    vi.mocked(deleteUser).mockResolvedValue(undefined);
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Ana Torres");
    await user.click(screen.getByRole("button", { name: /eliminar usuario ana torres/i }));
    await user.click(screen.getByRole("button", { name: /^eliminar usuario$/i }));

    await waitFor(() => expect(deleteUser).toHaveBeenCalledWith("user-other", 2));
    await waitFor(() => expect(getUsers).toHaveBeenCalledTimes(2));
  });

  it("AC4: no muestra la pestaña Eliminados (exclusiva de SuperAdmin)", async () => {
    vi.mocked(getUsers).mockResolvedValue([otherUser]);
    render(<Usuarios />);

    await screen.findByText("Ana Torres");
    expect(screen.queryByRole("button", { name: /^eliminados$/i })).not.toBeInTheDocument();
  });
});
