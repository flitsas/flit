// HU #10626 — Botón "Reenviar invitación" del menú de acciones (AdminCompany). AC1: reenvío
// exitoso con cooldown visual. AC2: 429 (cooldown anti-abuso real) mapeado a mensaje claro.
// AC3: el botón no aparece fuera del estado "Pendiente".
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";
import { getUsers, resendInvitation } from "@/lib/api/security";
import { ApiError } from "@/lib/api/types";
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

describe("Usuarios — botón Reenviar invitación (#10626, AdminCompany)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC3: no muestra el botón para usuarios que no están en estado Pendiente", async () => {
    vi.mocked(getUsers).mockResolvedValue([activeUser]);
    render(<Usuarios />);

    await screen.findByText("Ana Torres");
    expect(
      screen.queryByRole("button", { name: /reenviar invitación a ana torres/i }),
    ).not.toBeInTheDocument();
  });

  it("AC1: reenvío exitoso muestra confirmación y deja el botón deshabilitado (cooldown visual)", async () => {
    vi.mocked(getUsers).mockResolvedValue([pendingUser]);
    vi.mocked(resendInvitation).mockResolvedValue({
      invitationId: "invitation-1",
      email: "carlos@flit.local",
      emailSent: true,
    });
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Carlos Ruiz");
    const button = screen.getByRole("button", { name: /reenviar invitación a carlos ruiz/i });
    await user.click(button);

    expect(resendInvitation).toHaveBeenCalledWith("invitation-1");
    expect(await screen.findByText(/invitación reenviada a carlos@flit\.local/i)).toBeInTheDocument();
    expect(button).toBeDisabled();
  });

  it("AC2: reenvío antes del cooldown (429) muestra el mensaje de espera del backend", async () => {
    vi.mocked(getUsers).mockResolvedValue([pendingUser]);
    vi.mocked(resendInvitation).mockRejectedValue(
      new ApiError(429, "Error 429", {
        code: "RESEND_COOLDOWN_ACTIVE",
        message: "Debes esperar 1 minuto y 30 segundos antes de reenviar de nuevo.",
        retryAfterSeconds: 90,
      }),
    );
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Carlos Ruiz");
    const button = screen.getByRole("button", { name: /reenviar invitación a carlos ruiz/i });
    await user.click(button);

    expect(
      await screen.findByText(/debes esperar 1 minuto y 30 segundos antes de reenviar de nuevo/i),
    ).toBeInTheDocument();
    expect(button).toBeDisabled();
  });
});
