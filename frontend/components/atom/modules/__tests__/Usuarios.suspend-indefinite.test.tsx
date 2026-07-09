// Ajuste QA (ejecución manual del flujo completo del Feature #10618): el modal "Bloquear
// usuario" siempre exigía una fecha de fin, sin ofrecer la opción de desactivación indefinida
// del AC1 de la HU #10619 (el backend ya soporta EndsAt nulo — SuspendUserRequest.EndsAt es
// DateTimeOffset?). También cubre que un usuario suspendido ahora se ve como "Bloqueado" en la
// columna "Estado" (antes seguía mostrando "Activo", sin ninguna señal visible en la tabla).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";
import { getUsers, blockUser } from "@/lib/api/security";
import type { TenantUser } from "@/lib/api/security";

// Bloquear/desactivar es EXCLUSIVO de SuperAdmin, por eso se renderiza como SuperAdmin.
vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: true,
    isAdminCompany: false,
    isOtAdmin: false,
    permissions: [],
    tenantId: "tenant-1",
    userId: "user-self",
    roleId: "role-super",
    roleCode: "SuperAdmin",
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

describe("Usuarios — bloquear usuario (ajuste QA: desactivación indefinida, HU #10619 AC1)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("por defecto exige fecha de fin (comportamiento existente sin cambios)", async () => {
    vi.mocked(getUsers).mockResolvedValue([otherUser]);
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Ana Torres");
    await user.click(screen.getByRole("button", { name: /suspender temporalmente a ana torres/i }));

    expect(await screen.findByLabelText(/suspendido hasta/i)).toBeInTheDocument();
  });

  it("al marcar 'Desactivar indefinidamente' oculta la fecha de fin y llama a blockUser con endsAt=null", async () => {
    vi.mocked(getUsers).mockResolvedValue([otherUser]);
    vi.mocked(blockUser).mockResolvedValue({ id: "user-other" });
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Ana Torres");
    await user.click(screen.getByRole("button", { name: /suspender temporalmente a ana torres/i }));
    await user.type(
      await screen.findByLabelText(/motivo/i),
      "Desactivación indefinida de prueba",
    );
    await user.click(screen.getByRole("checkbox", { name: /sin fecha de fin/i }));

    expect(screen.queryByLabelText(/suspendido hasta/i)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /^desactivar usuario$/i }));

    await waitFor(() =>
      expect(blockUser).toHaveBeenCalledWith(
        "user-other",
        "Desactivación indefinida de prueba",
        null,
      ),
    );
  });

  it("muestra el chip 'Bloqueado' (no 'Activo') para un usuario suspendido", async () => {
    vi.mocked(getUsers).mockResolvedValue([{ ...otherUser, isSuspended: true }]);
    render(<Usuarios />);

    await screen.findByText("Ana Torres");
    expect(screen.getByText("Bloqueado")).toBeInTheDocument();
    expect(screen.queryByText("Activo")).not.toBeInTheDocument();
  });
});
