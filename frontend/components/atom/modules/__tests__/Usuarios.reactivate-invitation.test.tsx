// HU #11552 / ADR-0048 — invitación cancelada: estado vivo y reversible. AC1: la fila se ve en
// el listado con estado "Cancelada". AC2: el admin de compañía puede reactivarla (vuelve a
// "Pendiente"). AC3: si el correo ya se ocupó, mensaje explicativo y el estado NO cambia. AC4:
// la UI no ofrece Editar/Eliminar/Suspender/Restablecer contraseña/Ver historial sobre una fila
// cuyo `id` es un invitationId, no un userId.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";
import { getUsers, reactivateInvitation } from "@/lib/api/security";
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
  cancelInvitation: vi.fn(),
  reactivateInvitation: vi.fn(),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficeTenants: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

const cancelledInvitation: TenantUser = {
  id: "invitation-cancelled-1",
  fullName: "Sofía Nieto",
  email: "sofia@flit.local",
  role: null,
  roleCode: null,
  roleId: null,
  status: "cancelled",
  createdAt: "2026-07-01T00:00:00Z",
  isSuspended: false,
  rowVersion: 1,
};

describe("Usuarios — invitación cancelada, estado vivo y reversible (#11552, AdminCompany)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC1: la fila cancelada permanece visible con el badge 'Cancelada'", async () => {
    vi.mocked(getUsers).mockResolvedValue([cancelledInvitation]);
    render(<Usuarios />);

    await screen.findByText("Sofía Nieto");
    expect(screen.getByRole("status", { name: /^Estado: Cancelada$/i })).toBeInTheDocument();
  });

  it("AC4: no ofrece Editar/Eliminar/Suspender/Desactivar/Restablecer contraseña sobre una fila cancelada", async () => {
    vi.mocked(getUsers).mockResolvedValue([cancelledInvitation]);
    render(<Usuarios />);

    await screen.findByText("Sofía Nieto");
    expect(screen.queryByRole("button", { name: /editar usuario sofía nieto/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /eliminar usuario sofía nieto/i })).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /suspender temporalmente a sofía nieto/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /desactivar indefinidamente a sofía nieto/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /restablecer contraseña de sofía nieto/i }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /ver historial de sofía nieto/i })).not.toBeInTheDocument();
    // Tampoco "Cancelar invitación" — ya está cancelada, la acción que aplica es Reactivar.
    expect(
      screen.queryByRole("button", { name: /cancelar invitación a sofía nieto/i }),
    ).not.toBeInTheDocument();
  });

  it("AC2/AC4: sí ofrece 'Reactivar invitación' y, al usarla, la fila vuelve a verse Pendiente sin recargar la página", async () => {
    vi.mocked(getUsers)
      .mockResolvedValueOnce([cancelledInvitation])
      .mockResolvedValueOnce([{ ...cancelledInvitation, status: "pending" }]);
    vi.mocked(reactivateInvitation).mockResolvedValue({
      invitationId: "invitation-cancelled-1",
      email: "sofia@flit.local",
      emailSent: true,
    });
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Sofía Nieto");
    const button = screen.getByRole("button", { name: /reactivar invitación a sofía nieto/i });
    await user.click(button);

    expect(reactivateInvitation).toHaveBeenCalledWith("invitation-cancelled-1");
    // El refresco (loadUsers) trae la fila ya "pending" — el badge cambia sin recarga de
    // página. La fila se re-renderiza (deja de existir ReactivateInvitationButton y aparece
    // ResendInvitationButton), así que se verifica el resultado final, no el mensaje inline
    // efímero del botón que ya se desmontó.
    expect(await screen.findByRole("status", { name: /^Estado: Pendiente$/i })).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /reenviar invitación a sofía nieto/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /reactivar invitación a sofía nieto/i }),
    ).not.toBeInTheDocument();
  });

  it("AC3: 409 por correo ya ocupado muestra el mensaje unificado y el estado no cambia", async () => {
    vi.mocked(getUsers).mockResolvedValue([cancelledInvitation]);
    vi.mocked(reactivateInvitation).mockRejectedValue(
      new ApiError(409, "Error 409", { code: "USER_ALREADY_EXISTS", message: "irrelevante" }),
    );
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Sofía Nieto");
    const button = screen.getByRole("button", { name: /reactivar invitación a sofía nieto/i });
    await user.click(button);

    expect(
      await screen.findByText(/el correo utilizado ya se encuentra asociado a otra cuenta/i),
    ).toBeInTheDocument();
    // El estado sigue "Cancelada" — no se refresca en error.
    expect(screen.getByRole("status", { name: /^Estado: Cancelada$/i })).toBeInTheDocument();
  });

  it("409 INVITATION_NOT_CANCELLED muestra un mensaje explicativo distinto del conflicto de correo", async () => {
    vi.mocked(getUsers).mockResolvedValue([cancelledInvitation]);
    vi.mocked(reactivateInvitation).mockRejectedValue(
      new ApiError(409, "Error 409", { code: "INVITATION_NOT_CANCELLED", message: "irrelevante" }),
    );
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Sofía Nieto");
    const button = screen.getByRole("button", { name: /reactivar invitación a sofía nieto/i });
    await user.click(button);

    expect(
      await screen.findByText(/la invitación ya no está cancelada/i),
    ).toBeInTheDocument();
  });

  it("429 con cooldown deshabilita el botón y muestra el tiempo de espera real del backend", async () => {
    vi.mocked(getUsers).mockResolvedValue([cancelledInvitation]);
    vi.mocked(reactivateInvitation).mockRejectedValue(
      new ApiError(429, "Error 429", {
        code: "RESEND_COOLDOWN_ACTIVE",
        message: "Debes esperar 2 minutos antes de reactivar esta invitación de nuevo.",
        retryAfterSeconds: 120,
      }),
    );
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Sofía Nieto");
    const button = screen.getByRole("button", { name: /reactivar invitación a sofía nieto/i });
    await user.click(button);

    expect(
      await screen.findByText(/debes esperar 2 minutos antes de reactivar esta invitación de nuevo/i),
    ).toBeInTheDocument();
    expect(button).toBeDisabled();
  });

  it("filtro 'Cancelada' aísla las filas canceladas de las demás", async () => {
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
    vi.mocked(getUsers).mockResolvedValue([cancelledInvitation, activeUser]);
    const user = userEvent.setup();
    render(<Usuarios />);

    await screen.findByText("Sofía Nieto");
    expect(screen.getByText("Ana Torres")).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText("Filtrar por estado"), "cancelled");

    expect(screen.getByText("Sofía Nieto")).toBeInTheDocument();
    expect(screen.queryByText("Ana Torres")).not.toBeInTheDocument();
  });
});
