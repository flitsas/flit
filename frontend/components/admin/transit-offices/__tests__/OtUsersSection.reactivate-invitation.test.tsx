// HU #11552 / ADR-0048 — invitación cancelada en la pestaña "Usuarios" del hub OT. AC1: la fila
// se ve con estado "Cancelada". AC2: se puede reactivar (vuelve a "Pendiente"). AC3: correo ya
// ocupado → mensaje explicativo, sin cambio de estado. AC4: sin Editar/Eliminar/Suspender/Ver
// historial sobre una fila cuyo `id` es un invitationId.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import { fetchOtUsers, reactivateOtInvitation } from "@/lib/api/admin-ot-security";
import { ApiError } from "@/lib/api/types";
import type { OtUserItem } from "@/lib/api/admin-ot-security";

vi.mock("@/lib/api/admin-ot-security", () => ({
  fetchOtUsers: vi.fn(),
  inviteOtUser: vi.fn(),
  suspendOtUser: vi.fn(),
  unsuspendOtUser: vi.fn(),
  updateOtUser: vi.fn(),
  deleteOtUser: vi.fn(),
  resendOtInvitation: vi.fn(),
  cancelOtInvitation: vi.fn(),
  reactivateOtInvitation: vi.fn(),
}));

vi.mock("@/lib/api/security", () => ({
  restoreUser: vi.fn(),
  assignRole: vi.fn(),
  getRoles: vi.fn().mockResolvedValue([]),
}));

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: false,
    isAdminCompany: false,
    isOtAdmin: true,
    permissions: [],
    tenantId: "ot-tenant-1",
    userId: "u-self",
    roleId: "role-1",
    roleCode: "ot_admin",
  }),
}));

function renderSection() {
  return render(
    <ToastProvider>
      <OtUsersSection transitOfficeId="ot-1" />
    </ToastProvider>,
  );
}

const cancelledInvitation: OtUserItem = {
  id: "invitation-ot-cancelled",
  fullName: "Rita Peña",
  email: "rita@transito.gov.co",
  role: null,
  roleCode: null,
  roleId: null,
  status: "cancelled",
  createdAt: "2026-07-01T00:00:00Z",
  isSuspended: false,
  rowVersion: 1,
};

describe("OtUsersSection — invitación cancelada, estado vivo y reversible (#11552)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC1: la fila cancelada permanece visible con el badge 'Cancelada'", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [cancelledInvitation] });
    renderSection();

    await screen.findByText("Rita Peña");
    expect(screen.getByRole("status", { name: /^Estado: Cancelada$/i })).toBeInTheDocument();
  });

  it("AC4: no ofrece Editar/Eliminar/Ver historial sobre una fila cancelada (ot_admin)", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [cancelledInvitation] });
    renderSection();

    await screen.findByText("Rita Peña");
    expect(screen.queryByRole("button", { name: /editar usuario rita peña/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /eliminar usuario rita peña/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /ver historial de rita peña/i })).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /cancelar invitación a rita peña/i }),
    ).not.toBeInTheDocument();
  });

  it("AC2: reactivar exitosamente liga el scope OT, refresca la lista y muestra un toast", async () => {
    vi.mocked(fetchOtUsers)
      .mockResolvedValueOnce({ data: [cancelledInvitation] })
      .mockResolvedValueOnce({ data: [{ ...cancelledInvitation, status: "pending" }] });
    vi.mocked(reactivateOtInvitation).mockResolvedValue({
      invitationId: "invitation-ot-cancelled",
      email: "rita@transito.gov.co",
      emailSent: true,
    });
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("Rita Peña");
    const button = screen.getByRole("button", { name: /reactivar invitación a rita peña/i });
    await user.click(button);

    expect(reactivateOtInvitation).toHaveBeenCalledWith("invitation-ot-cancelled", {
      transitOfficeId: "ot-1",
    });
    expect(
      await screen.findByText(/invitación reactivada y reenviada a rita@transito\.gov\.co/i),
    ).toBeInTheDocument();
    expect(await screen.findByRole("status", { name: /^Estado: Pendiente$/i })).toBeInTheDocument();
  });

  it("AC3: 409 por correo ya ocupado muestra el mensaje unificado y el estado no cambia", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [cancelledInvitation] });
    vi.mocked(reactivateOtInvitation).mockRejectedValue(
      new ApiError(409, "Error 409", { error: "EMAIL_BELONGS_TO_DELETED_USER", message: "irrelevante" }),
    );
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("Rita Peña");
    const button = screen.getByRole("button", { name: /reactivar invitación a rita peña/i });
    await user.click(button);

    expect(
      await screen.findByText(/el correo utilizado ya se encuentra asociado a otra cuenta/i),
    ).toBeInTheDocument();
    expect(screen.getByRole("status", { name: /^Estado: Cancelada$/i })).toBeInTheDocument();
  });

  it("429 con cooldown deshabilita el botón (campo 'error', no 'code' — ruta OT)", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [cancelledInvitation] });
    vi.mocked(reactivateOtInvitation).mockRejectedValue(
      new ApiError(429, "Error 429", {
        error: "RESEND_COOLDOWN_ACTIVE",
        message: "Debes esperar 30 segundos antes de reactivar esta invitación de nuevo.",
        retryAfterSeconds: 30,
      }),
    );
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("Rita Peña");
    const button = screen.getByRole("button", { name: /reactivar invitación a rita peña/i });
    await user.click(button);

    expect(
      await screen.findByText(/debes esperar 30 segundos antes de reactivar esta invitación de nuevo/i),
    ).toBeInTheDocument();
    expect(button).toBeDisabled();
  });
});
