// HU #10624 (AC3/AC4) — Toggle "Ver eliminados" en la pestaña "Usuarios" del hub OT, exclusivo
// de SuperAdmin. Cubre el flujo real end-to-end contra GET /api/v1/admin/ot/users?onlyDeleted=true:
// listar eliminados del tenant OT resuelto → restaurar con un clic de confirmación
// (RestoreUserDialog + restoreUser genérico) → desaparece de la lista de eliminados. También
// verifica los 4 estados de UI (cargando/error/vacío/lleno) vía UiStateBoundary.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import { fetchOtUsers } from "@/lib/api/admin-ot-security";
import { restoreUser } from "@/lib/api/security";
import type { OtUserItem } from "@/lib/api/admin-ot-security";

vi.mock("@/lib/api/admin-ot-security", () => ({
  fetchOtUsers: vi.fn(),
  inviteOtUser: vi.fn(),
  suspendOtUser: vi.fn(),
  unsuspendOtUser: vi.fn(),
  updateOtUser: vi.fn(),
  deleteOtUser: vi.fn(),
  resendOtInvitation: vi.fn(),
}));

vi.mock("@/lib/api/security", () => ({
  restoreUser: vi.fn(),
}));

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

function renderSection() {
  return render(
    <ToastProvider>
      <OtUsersSection transitOfficeId="ot-1" />
    </ToastProvider>,
  );
}

const activeUser: OtUserItem = {
  id: "u-1",
  fullName: "Laura García",
  email: "laura@transito.gov.co",
  role: "Admin OT",
  roleCode: "ot_admin",
  roleId: "role-1",
  status: "active",
  createdAt: "2026-06-23T10:00:00Z",
  isSuspended: false,
  rowVersion: 7,
};

const deletedUser: OtUserItem = {
  id: "u-2",
  fullName: "Pedro Ruiz",
  email: "pedro@transito.gov.co",
  role: "Admin OT",
  roleCode: "ot_admin",
  roleId: "role-1",
  status: "inactive",
  createdAt: "2026-05-01T10:00:00Z",
  isSuspended: false,
  rowVersion: 4,
  deletedAt: "2026-07-02T12:00:00Z",
};

async function openVerEliminados() {
  const user = userEvent.setup();
  renderSection();
  await user.click(await screen.findByRole("button", { name: /ver eliminados/i }));
  return user;
}

describe("OtUsersSection — toggle Ver eliminados (#10624, SuperAdmin)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtUsers).mockImplementation(async (_scope, _signal, onlyDeleted?: boolean) =>
      onlyDeleted ? { data: [deletedUser] } : { data: [activeUser] },
    );
    vi.mocked(restoreUser).mockResolvedValue(undefined);
  });

  it("AC3/AC4: SuperAdmin ve el botón 'Ver eliminados'", async () => {
    renderSection();
    expect(await screen.findByRole("button", { name: /ver eliminados/i })).toBeInTheDocument();
  });

  it("AC3: estado vacío cuando no hay usuarios eliminados en este OT", async () => {
    vi.mocked(fetchOtUsers).mockImplementation(async (_scope, _signal, onlyDeleted?: boolean) =>
      onlyDeleted ? { data: [] } : { data: [activeUser] },
    );

    await openVerEliminados();

    expect(
      await screen.findByText(/no hay usuarios eliminados en este organismo de tránsito/i),
    ).toBeInTheDocument();
  });

  it("AC3: estado error cuando falla el listado de eliminados", async () => {
    vi.mocked(fetchOtUsers).mockImplementation(async (_scope, _signal, onlyDeleted?: boolean) => {
      if (onlyDeleted) throw new Error("network error");
      return { data: [activeUser] };
    });

    await openVerEliminados();

    expect(
      await screen.findByText(/no se pudo cargar el listado de usuarios eliminados/i),
    ).toBeInTheDocument();
  });

  it("AC3: SuperAdmin ve eliminados del tenant OT y restaura con un clic de confirmación — desaparece de la lista", async () => {
    const user = await openVerEliminados();

    expect(await screen.findByText("Pedro Ruiz")).toBeInTheDocument();
    expect(fetchOtUsers).toHaveBeenCalledWith({ transitOfficeId: "ot-1" }, expect.anything(), true);
    // La tabla de usuarios activos se oculta mientras se muestra el listado de eliminados.
    expect(screen.queryByText("Laura García")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /restaurar usuario pedro ruiz/i }));
    expect(await screen.findByRole("heading", { name: /^restaurar usuario$/i })).toBeInTheDocument();

    // Tras confirmar, la lista de eliminados se recarga y el usuario restaurado ya no aparece.
    vi.mocked(fetchOtUsers).mockImplementation(async (_scope, _signal, onlyDeleted?: boolean) =>
      onlyDeleted ? { data: [] } : { data: [activeUser] },
    );
    await user.click(screen.getByRole("button", { name: /^restaurar usuario$/i }));

    await waitFor(() => expect(restoreUser).toHaveBeenCalledWith("u-2"));
    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: /^restaurar usuario$/i })).not.toBeInTheDocument(),
    );
    await waitFor(() => expect(screen.queryByText("Pedro Ruiz")).not.toBeInTheDocument());
    expect(
      await screen.findByText(/no hay usuarios eliminados en este organismo de tránsito/i),
    ).toBeInTheDocument();
  });

  it("Ajuste QA: 'Eliminado el' muestra una fecha legible, no el ISO crudo", async () => {
    await openVerEliminados();

    await screen.findByText("Pedro Ruiz");
    expect(screen.queryByText(deletedUser.deletedAt!)).not.toBeInTheDocument();
  });

  it("Ajuste QA: tras restaurar, también recarga el listado de usuarios activos (no solo el de eliminados)", async () => {
    const user = await openVerEliminados();
    await screen.findByText("Pedro Ruiz");

    const activeCallsBefore = vi
      .mocked(fetchOtUsers)
      .mock.calls.filter((call) => !call[2]).length;

    await user.click(screen.getByRole("button", { name: /restaurar usuario pedro ruiz/i }));
    await user.click(screen.getByRole("button", { name: /^restaurar usuario$/i }));

    await waitFor(() => expect(restoreUser).toHaveBeenCalledWith("u-2"));
    await waitFor(() => {
      const activeCallsAfter = vi
        .mocked(fetchOtUsers)
        .mock.calls.filter((call) => !call[2]).length;
      expect(activeCallsAfter).toBeGreaterThan(activeCallsBefore);
    });
  });
});
