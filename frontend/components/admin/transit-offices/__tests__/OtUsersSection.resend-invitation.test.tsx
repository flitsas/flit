// HU #10626 — Botón "Reenviar invitación" en la pestaña "Usuarios" del hub OT. AC1: reenvío
// exitoso con cooldown visual (+ toast, patrón ya usado en este componente). AC2: 429 mapeado a
// mensaje claro. AC3: el botón no aparece fuera del estado "Pendiente".
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import { fetchOtUsers, resendOtInvitation } from "@/lib/api/admin-ot-security";
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

const pendingUser: OtUserItem = {
  id: "invitation-ot-1",
  fullName: "Marta Gómez",
  email: "marta@transito.gov.co",
  role: null,
  roleCode: null,
  roleId: null,
  status: "pending",
  createdAt: "2026-07-01T00:00:00Z",
  isSuspended: false,
  rowVersion: 1,
};

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

describe("OtUsersSection — botón Reenviar invitación (#10626)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC3: no muestra el botón para usuarios que no están en estado Pendiente", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    renderSection();

    await screen.findByText("Laura García");
    expect(
      screen.queryByRole("button", { name: /reenviar invitación a laura garcía/i }),
    ).not.toBeInTheDocument();
  });

  it("AC1: reenvío exitoso llama a resendOtInvitation con el scope OT, confirma y deshabilita el botón", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [pendingUser] });
    vi.mocked(resendOtInvitation).mockResolvedValue({
      invitationId: "invitation-ot-1",
      email: "marta@transito.gov.co",
      emailSent: true,
    });
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("Marta Gómez");
    const button = screen.getByRole("button", { name: /reenviar invitación a marta gómez/i });
    await user.click(button);

    expect(resendOtInvitation).toHaveBeenCalledWith("invitation-ot-1", { transitOfficeId: "ot-1" });
    // El mensaje aparece dos veces: inline (junto al botón) y en el toast global (onResent) —
    // mismo patrón que el resto de acciones de este componente.
    expect(await screen.findAllByText(/invitación reenviada a marta@transito\.gov\.co/i)).toHaveLength(2);
    expect(button).toBeDisabled();
  });

  it("AC2: reenvío antes del cooldown (429) muestra el mensaje de espera del backend", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [pendingUser] });
    vi.mocked(resendOtInvitation).mockRejectedValue(
      new ApiError(429, "Error 429", {
        error: "RESEND_COOLDOWN_ACTIVE",
        message: "Debes esperar 45 segundos antes de reenviar de nuevo.",
        retryAfterSeconds: 45,
      }),
    );
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("Marta Gómez");
    const button = screen.getByRole("button", { name: /reenviar invitación a marta gómez/i });
    await user.click(button);

    expect(
      await screen.findByText(/debes esperar 45 segundos antes de reenviar de nuevo/i),
    ).toBeInTheDocument();
    expect(button).toBeDisabled();
  });
});
