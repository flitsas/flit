// HU #10628 — Botón "Cancelar invitación" en la pestaña "Usuarios" del hub OT. AC1: cancelación
// exitosa hace desaparecer la fila. AC2: nunca coexiste con "Eliminar usuario" en la misma fila
// (Pendiente o no). AC3: 404/409 al confirmar (condición de carrera) refresca el listado.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import { fetchOtUsers, cancelOtInvitation } from "@/lib/api/admin-ot-security";
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

describe("OtUsersSection — botón Cancelar invitación (#10628)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC2: SOLO muestra 'Cancelar invitación' (no 'Eliminar usuario') en una fila Pendiente", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [pendingUser] });
    renderSection();

    await screen.findByText("Marta Gómez");
    expect(
      screen.getByRole("button", { name: /cancelar invitación a marta gómez/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /eliminar usuario marta gómez/i }),
    ).not.toBeInTheDocument();
  });

  it("AC2: SOLO muestra 'Eliminar usuario' (no 'Cancelar invitación') en una fila NO Pendiente", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    renderSection();

    await screen.findByText("Laura García");
    expect(
      screen.getByRole("button", { name: /eliminar usuario laura garcía/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /cancelar invitación a laura garcía/i }),
    ).not.toBeInTheDocument();
  });

  it("AC1: cancelación exitosa llama a cancelOtInvitation con el scope OT y la fila desaparece", async () => {
    vi.mocked(fetchOtUsers)
      .mockResolvedValueOnce({ data: [pendingUser] })
      .mockResolvedValueOnce({ data: [] });
    vi.mocked(cancelOtInvitation).mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("Marta Gómez");
    await user.click(screen.getByRole("button", { name: /cancelar invitación a marta gómez/i }));
    await user.click(screen.getByRole("button", { name: /^cancelar invitación$/i }));

    await waitFor(() =>
      expect(cancelOtInvitation).toHaveBeenCalledWith("invitation-ot-1", { transitOfficeId: "ot-1" }),
    );
    await waitFor(() => expect(screen.queryByText("Marta Gómez")).not.toBeInTheDocument());
    expect(await screen.findByText(/invitación cancelada correctamente/i)).toBeInTheDocument();
  });

  it("AC3: si la invitación ya no existe (404, campo `error` de OT) muestra el mensaje y refresca el listado", async () => {
    vi.mocked(fetchOtUsers)
      .mockResolvedValueOnce({ data: [pendingUser] })
      .mockResolvedValueOnce({ data: [] });
    vi.mocked(cancelOtInvitation).mockRejectedValue(
      new ApiError(404, "Not Found", { error: "INVITATION_NOT_FOUND" }),
    );
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("Marta Gómez");
    await user.click(screen.getByRole("button", { name: /cancelar invitación a marta gómez/i }));
    await user.click(screen.getByRole("button", { name: /^cancelar invitación$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/ya no existe/i);
    await waitFor(() => expect(fetchOtUsers).toHaveBeenCalledTimes(2));
  });

  it("AC3: si la invitación ya no está pendiente (409) muestra el mensaje y refresca el listado", async () => {
    vi.mocked(fetchOtUsers)
      .mockResolvedValueOnce({ data: [pendingUser] })
      .mockResolvedValueOnce({ data: [] });
    vi.mocked(cancelOtInvitation).mockRejectedValue(
      new ApiError(409, "Conflict", { error: "INVITATION_NOT_PENDING" }),
    );
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("Marta Gómez");
    await user.click(screen.getByRole("button", { name: /cancelar invitación a marta gómez/i }));
    await user.click(screen.getByRole("button", { name: /^cancelar invitación$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/ya no está pendiente/i);
    await waitFor(() => expect(fetchOtUsers).toHaveBeenCalledTimes(2));
  });
});
