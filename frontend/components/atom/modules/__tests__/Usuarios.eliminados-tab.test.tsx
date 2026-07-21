// HU #10624 (AC3) — Pestaña "Eliminados", exclusiva de SuperAdmin (AC4). Cubre el flujo real
// end-to-end contra GET /api/v1/security/users?onlyDeleted=true: listar eliminados de
// cualquier tenant → restaurar con un clic de confirmación (RestoreUserDialog + restoreUser) →
// desaparece de la lista de eliminados. Los 4 estados de UI (cargando/error/vacío/lleno) también
// se verifican explícitamente.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";
import { getUsers, restoreUser } from "@/lib/api/security";
import type { TenantUser } from "@/lib/api/security";

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

const deletedUser: TenantUser = {
  id: "user-1",
  fullName: "Laura García",
  email: "laura@flit.local",
  role: "AdminCompany",
  roleCode: "admin_company",
  roleId: "role-1",
  status: "inactive",
  createdAt: "2026-01-10T10:00:00Z",
  isSuspended: false,
  tenantId: "tenant-1",
  tenantName: "Transportes Andina S.A.S.",
  rowVersion: 3,
  deletedAt: "2026-07-01T09:30:00Z",
};

async function openEliminadosTab() {
  const user = userEvent.setup();
  render(<Usuarios />);
  await user.click(await screen.findByRole("button", { name: /^eliminados$/i }));
  return user;
}

describe("Usuarios — pestaña Eliminados (#10624, SuperAdmin)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getUsers).mockImplementation(async (onlyDeleted?: boolean) =>
      onlyDeleted ? [deletedUser] : [],
    );
    vi.mocked(restoreUser).mockResolvedValue(undefined);
  });

  it("AC3/AC4: SuperAdmin ve la pestaña Eliminados", async () => {
    render(<Usuarios />);
    expect(await screen.findByRole("button", { name: /^eliminados$/i })).toBeInTheDocument();
  });

  it("AC3: estado cargando mientras se resuelve onlyDeleted=true", async () => {
    let resolveUsers!: (value: TenantUser[]) => void;
    vi.mocked(getUsers).mockImplementation(
      (onlyDeleted?: boolean) =>
        onlyDeleted
          ? new Promise<TenantUser[]>((resolve) => {
              resolveUsers = resolve;
            })
          : Promise.resolve([]),
    );

    await openEliminadosTab();

    expect(screen.getByText(/cargando usuarios eliminados/i)).toBeInTheDocument();
    resolveUsers([]);
    await waitFor(() => expect(screen.queryByText(/cargando usuarios eliminados/i)).not.toBeInTheDocument());
  });

  it("AC3: estado vacío cuando no hay usuarios eliminados", async () => {
    vi.mocked(getUsers).mockImplementation(async (onlyDeleted?: boolean) => (onlyDeleted ? [] : []));

    await openEliminadosTab();

    expect(
      await screen.findByText(/no hay usuarios eliminados de ninguna compañía u organismo/i),
    ).toBeInTheDocument();
  });

  it("AC3: estado error cuando falla el listado", async () => {
    vi.mocked(getUsers).mockImplementation(async (onlyDeleted?: boolean) => {
      if (onlyDeleted) throw new Error("network error");
      return [];
    });

    await openEliminadosTab();

    expect(await screen.findByRole("alert")).toHaveTextContent(/error al cargar usuarios eliminados/i);
  });

  it("AC3: SuperAdmin ve eliminados de cualquier tenant (tenantName visible) y restaura con un clic de confirmación — desaparece de la lista", async () => {
    const user = await openEliminadosTab();

    expect(await screen.findByText("Laura García")).toBeInTheDocument();
    expect(screen.getByText("Transportes Andina S.A.S.")).toBeInTheDocument();
    expect(getUsers).toHaveBeenCalledWith(true);

    await user.click(screen.getByRole("button", { name: /restaurar usuario laura garcía/i }));
    expect(await screen.findByRole("heading", { name: /^restaurar usuario$/i })).toBeInTheDocument();

    // Tras confirmar, la lista de eliminados se recarga y el usuario restaurado ya no aparece.
    vi.mocked(getUsers).mockImplementation(async (onlyDeleted?: boolean) => (onlyDeleted ? [] : []));
    await user.click(screen.getByRole("button", { name: /^restaurar usuario$/i }));

    await waitFor(() => expect(restoreUser).toHaveBeenCalledWith("user-1"));
    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: /^restaurar usuario$/i })).not.toBeInTheDocument(),
    );
    await waitFor(() => expect(screen.queryByText("Laura García")).not.toBeInTheDocument());
    expect(
      await screen.findByText(/no hay usuarios eliminados de ninguna compañía u organismo/i),
    ).toBeInTheDocument();
  });

  it("Ajuste QA: 'Eliminado el' muestra una fecha legible, no el ISO crudo", async () => {
    await openEliminadosTab();

    await screen.findByText("Laura García");
    expect(screen.queryByText(deletedUser.deletedAt!)).not.toBeInTheDocument();
  });

  it("Ajuste QA: tras restaurar, también recarga el listado de 'Usuarios' (no solo 'Eliminados')", async () => {
    const user = await openEliminadosTab();
    await screen.findByText("Laura García");

    const activeCallsBefore = vi.mocked(getUsers).mock.calls.filter((call) => call[0] !== true).length;

    await user.click(screen.getByRole("button", { name: /restaurar usuario laura garcía/i }));
    await user.click(screen.getByRole("button", { name: /^restaurar usuario$/i }));

    await waitFor(() => expect(restoreUser).toHaveBeenCalledWith("user-1"));
    await waitFor(() => {
      const activeCallsAfter = vi.mocked(getUsers).mock.calls.filter((call) => call[0] !== true).length;
      expect(activeCallsAfter).toBeGreaterThan(activeCallsBefore);
    });
  });

  it("SuperAdmin ve el botón Eliminar en la tabla de Usuarios (bloquear/eliminar es exclusivo de SuperAdmin)", async () => {
    const activeUser: TenantUser = {
      ...deletedUser,
      id: "active-1",
      fullName: "Carlos Ruiz",
      status: "active",
      deletedAt: undefined,
    };
    vi.mocked(getUsers).mockImplementation(async (onlyDeleted?: boolean) =>
      onlyDeleted ? [deletedUser] : [activeUser],
    );
    render(<Usuarios />);
    await screen.findByText("Carlos Ruiz");
    expect(
      screen.getByRole("button", { name: /eliminar usuario carlos ruiz/i }),
    ).toBeInTheDocument();
    // Y también las acciones de bloqueo (suspender / desactivar), exclusivas de SuperAdmin.
    expect(
      screen.getByRole("button", { name: /suspender temporalmente a carlos ruiz/i }),
    ).toBeInTheDocument();
  });
});
