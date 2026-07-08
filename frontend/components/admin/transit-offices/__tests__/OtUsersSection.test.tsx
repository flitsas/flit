// Refactor adminOT — pestaña "Usuarios" del hub OT: 4 estados de UI + invitar +
// suspender/reactivar (self-service, sin selector de rol).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import type { OtUserItem } from "@/lib/api/admin-ot-security";

vi.mock("@/lib/api/admin-ot-security", () => ({
  fetchOtUsers: vi.fn(),
  inviteOtUser: vi.fn(),
  suspendOtUser: vi.fn(),
  unsuspendOtUser: vi.fn(),
  updateOtUser: vi.fn(),
  deleteOtUser: vi.fn(),
}));

import {
  fetchOtUsers,
  inviteOtUser,
  suspendOtUser,
  unsuspendOtUser,
} from "@/lib/api/admin-ot-security";

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
  rowVersion: 1,
};

const suspendedUser: OtUserItem = {
  id: "u-2",
  fullName: "Carlos Pérez",
  email: "carlos@transito.gov.co",
  role: "Admin OT",
  roleCode: "ot_admin",
  roleId: "role-1",
  status: "active",
  createdAt: "2026-06-20T10:00:00Z",
  isSuspended: true,
  rowVersion: 5,
};

function renderSection() {
  return render(
    <ToastProvider>
      <OtUsersSection transitOfficeId="ot-1" />
    </ToastProvider>,
  );
}

describe("OtUsersSection — refactor adminOT", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("estado cargando: muestra el skeleton", () => {
    vi.mocked(fetchOtUsers).mockReturnValue(new Promise(() => {}));
    renderSection();
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
  });

  it("estado vacío: sin usuarios muestra CTA de invitar", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [] });
    renderSection();
    expect(await screen.findByTestId("ui-empty")).toBeInTheDocument();
    expect(screen.getByText(/No hay usuarios en este organismo/i)).toBeInTheDocument();
  });

  it("estado error: muestra reintentar y vuelve a cargar", async () => {
    vi.mocked(fetchOtUsers).mockRejectedValueOnce(new Error("network"));
    const user = userEvent.setup();
    renderSection();
    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();

    vi.mocked(fetchOtUsers).mockResolvedValueOnce({ data: [activeUser] });
    await user.click(screen.getByRole("button", { name: /Reintentar/i }));
    expect(await screen.findByText("Laura García")).toBeInTheDocument();
  });

  it("estado lleno: lista usuarios con su estado", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser, suspendedUser] });
    renderSection();
    expect(await screen.findByText("Laura García")).toBeInTheDocument();
    expect(screen.getByText("Carlos Pérez")).toBeInTheDocument();
    expect(screen.getByText("Activo")).toBeInTheDocument();
    expect(screen.getByText("Suspendido")).toBeInTheDocument();
  });

  it("invita a un usuario nuevo (solo email + nombre, sin selector de rol)", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    vi.mocked(inviteOtUser).mockResolvedValue({
      invitationId: "inv-1",
      email: "nuevo@transito.gov.co",
      emailSent: true,
    });
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Laura García");

    await user.click(screen.getByRole("button", { name: /Invitar usuario/i }));
    expect(screen.queryByRole("combobox", { name: /rol/i })).not.toBeInTheDocument();

    await user.type(screen.getByLabelText(/Nombre completo/i), "Nuevo Colaborador");
    await user.type(screen.getByLabelText(/Correo electrónico/i), "nuevo@transito.gov.co");
    await user.click(screen.getByRole("button", { name: /Enviar invitación/i }));

    await waitFor(() =>
      expect(inviteOtUser).toHaveBeenCalledWith(
        { email: "nuevo@transito.gov.co", fullName: "Nuevo Colaborador" },
        { transitOfficeId: "ot-1" },
      ),
    );
  });

  it("suspende a un usuario activo", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    vi.mocked(suspendOtUser).mockResolvedValue({ id: "sus-1" });
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Laura García");

    await user.click(screen.getByRole("button", { name: /Suspender usuario Laura García/i }));
    await user.type(screen.getByLabelText(/Motivo/i), "Incumplimiento");
    await user.click(screen.getByRole("button", { name: /^Suspender$/i }));

    await waitFor(() => expect(suspendOtUser).toHaveBeenCalled());
    expect(vi.mocked(suspendOtUser).mock.calls[0]?.[0]).toBe("u-1");
    expect(vi.mocked(suspendOtUser).mock.calls[0]?.[2]).toEqual({ transitOfficeId: "ot-1" });
  });

  it("reactiva a un usuario suspendido", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [suspendedUser] });
    vi.mocked(unsuspendOtUser).mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Carlos Pérez");

    await user.click(screen.getByRole("button", { name: /Reactivar usuario Carlos Pérez/i }));
    await waitFor(() =>
      expect(unsuspendOtUser).toHaveBeenCalledWith("u-2", { transitOfficeId: "ot-1" }),
    );
  });
});
