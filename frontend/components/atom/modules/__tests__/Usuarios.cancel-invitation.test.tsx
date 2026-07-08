// HU #10628 — Botón "Cancelar invitación" del menú de acciones (AdminCompany). AC1: cancelación
// exitosa hace desaparecer la fila. AC2: nunca coexiste con "Eliminar usuario" en la misma fila
// (Pendiente o no). AC3: 404/409 al confirmar (condición de carrera) refresca el listado.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";
import { getUsers, cancelInvitation } from "@/lib/api/security";
import { ApiError } from "@/lib/api/types";
import type { TenantUser } from "@/lib/api/security";

// "Cancelar invitación" está disponible para AdminCompany (gestión de invitaciones), pero
// "Eliminar usuario" ahora es exclusivo de SuperAdmin. Por eso los permisos son mutables: el
// caso "fila NO pendiente muestra Eliminar" se prueba como SuperAdmin.
const ADMIN_COMPANY_PERMS = {
  isSuperAdmin: false,
  isAdminCompany: true,
  isOtAdmin: false,
  permissions: [] as string[],
  tenantId: "tenant-1",
  userId: "user-self",
  roleId: "role-admin",
  roleCode: "AdminCompany",
};
const SUPER_ADMIN_PERMS = {
  ...ADMIN_COMPANY_PERMS,
  isSuperAdmin: true,
  isAdminCompany: false,
  roleId: "role-super",
  roleCode: "SuperAdmin",
};

const perms = vi.hoisted(() => ({ current: {} as Record<string, unknown> }));

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => perms.current,
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
  cancelInvitation: vi.fn(),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficeTenants: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

const pendingUser: TenantUser = {
  id: "invitation-1",
  fullName: "Carlos Ruiz",
  email: "carlos@flit.local",
  role: null,
  roleCode: null,
  roleId: null,
  status: "pending",
  createdAt: "2026-07-01T00:00:00Z",
  isSuspended: false,
  rowVersion: 1,
};

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

describe("Usuarios — botón Cancelar invitación (#10628)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    perms.current = { ...ADMIN_COMPANY_PERMS };
  });

  it("AC2: SOLO muestra 'Cancelar invitación' (no 'Eliminar usuario') en una fila Pendiente", async () => {
    vi.mocked(getUsers).mockResolvedValue([pendingUser]);
    render(<Usuarios />);

    await screen.findByText("Carlos Ruiz");
    expect(
      screen.getByRole("button", { name: /cancelar invitación a carlos ruiz/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /eliminar usuario carlos ruiz/i }),
    ).not.toBeInTheDocument();
  });

  it("AC2: SOLO muestra 'Eliminar usuario' (no 'Cancelar invitación') en una fila NO Pendiente (SuperAdmin)", async () => {
    perms.current = { ...SUPER_ADMIN_PERMS };
    vi.mocked(getUsers).mockResolvedValue([activeUser]);
    render(<Usuarios />);

    await screen.findByText("Ana Torres");
    expect(
      screen.getByRole("button", { name: /eliminar usuario ana torres/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /cancelar invitación a ana torres/i }),
    ).not.toBeInTheDocument();
  });

  it("AC1: cancelación exitosa llama a cancelInvitation y la fila desaparece de la lista", async () => {
    vi.mocked(getUsers)
      .mockResolvedValueOnce([pendingUser])
      .mockResolvedValueOnce([]);
    vi.mocked(cancelInvitation).mockResolvedValue(undefined);
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Carlos Ruiz");
    await user.click(screen.getByRole("button", { name: /cancelar invitación a carlos ruiz/i }));
    await user.click(screen.getByRole("button", { name: /^cancelar invitación$/i }));

    await waitFor(() => expect(cancelInvitation).toHaveBeenCalledWith("invitation-1"));
    await waitFor(() => expect(screen.queryByText("Carlos Ruiz")).not.toBeInTheDocument());
  });

  it("AC3: si la invitación ya no existe (404) muestra el mensaje y refresca el listado (sin fila fantasma)", async () => {
    vi.mocked(getUsers)
      .mockResolvedValueOnce([pendingUser])
      .mockResolvedValueOnce([]);
    vi.mocked(cancelInvitation).mockRejectedValue(
      new ApiError(404, "Not Found", { code: "INVITATION_NOT_FOUND" }),
    );
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Carlos Ruiz");
    await user.click(screen.getByRole("button", { name: /cancelar invitación a carlos ruiz/i }));
    await user.click(screen.getByRole("button", { name: /^cancelar invitación$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/ya no existe/i);
    await waitFor(() => expect(getUsers).toHaveBeenCalledTimes(2));
  });

  it("AC3: si la invitación ya no está pendiente (409) muestra el mensaje y refresca el listado", async () => {
    vi.mocked(getUsers)
      .mockResolvedValueOnce([pendingUser])
      .mockResolvedValueOnce([]);
    vi.mocked(cancelInvitation).mockRejectedValue(
      new ApiError(409, "Conflict", { code: "INVITATION_NOT_PENDING" }),
    );
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Carlos Ruiz");
    await user.click(screen.getByRole("button", { name: /cancelar invitación a carlos ruiz/i }));
    await user.click(screen.getByRole("button", { name: /^cancelar invitación$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/ya no está pendiente/i);
    await waitFor(() => expect(getUsers).toHaveBeenCalledTimes(2));
  });
});
