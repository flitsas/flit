// HU #10622 — Botón "Editar" por usuario en la tabla de Usuarios (Compañía). AC1: abre el
// modal con el formulario prellenado. AC4: no aparece para usuarios con estado "Pendiente"
// (sin cuenta real todavía).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";
import { getUsers, updateUser } from "@/lib/api/security";
import type { TenantUser } from "@/lib/api/security";

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: false,
    isAdminCompany: true,
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
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficeTenants: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

const activeUser: TenantUser = {
  id: "user-active",
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

const pendingUser: TenantUser = {
  id: "user-pending",
  fullName: "Invitado Sin Cuenta",
  email: "invitado@flit.local",
  role: null,
  roleCode: null,
  roleId: null,
  status: "pending",
  createdAt: null,
  isSuspended: false,
  rowVersion: 0,
};

describe("Usuarios — botón Editar (#10622)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC4: no muestra el botón Editar para un usuario con estado Pendiente", async () => {
    vi.mocked(getUsers).mockResolvedValue([pendingUser]);
    render(<Usuarios />);

    await screen.findByText("Invitado Sin Cuenta");
    expect(
      screen.queryByRole("button", { name: /editar usuario invitado sin cuenta/i }),
    ).not.toBeInTheDocument();
  });

  it("AC1: abre el modal prellenado con nombre y correo al hacer clic en Editar de un usuario activo", async () => {
    vi.mocked(getUsers).mockResolvedValue([activeUser]);
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Ana Torres");
    await user.click(screen.getByRole("button", { name: /editar usuario ana torres/i }));

    expect(await screen.findByLabelText(/nombre completo/i)).toHaveValue("Ana Torres");
    expect(screen.getByLabelText(/correo electrónico/i)).toHaveValue("ana@flit.local");
  });

  it("guarda los cambios llamando a updateUser con el rowVersion leído y recarga el listado", async () => {
    vi.mocked(getUsers).mockResolvedValue([activeUser]);
    vi.mocked(updateUser).mockResolvedValue(undefined);
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Ana Torres");
    await user.click(screen.getByRole("button", { name: /editar usuario ana torres/i }));

    const nameInput = await screen.findByLabelText(/nombre completo/i);
    await user.clear(nameInput);
    await user.type(nameInput, "Ana Torres Ruiz");
    await user.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() =>
      expect(updateUser).toHaveBeenCalledWith("user-active", {
        displayName: "Ana Torres Ruiz",
        email: "ana@flit.local",
        rowVersion: 2,
      }),
    );
    await waitFor(() => expect(getUsers).toHaveBeenCalledTimes(2));
  });
});
