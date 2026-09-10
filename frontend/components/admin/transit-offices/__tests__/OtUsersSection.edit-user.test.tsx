// HU #10622 — Botón "Editar" por usuario en la pestaña "Usuarios" del hub OT. AC1: abre el
// modal con el formulario prellenado. AC4: no aparece para usuarios con estado "Pendiente".
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import { fetchOtUsers, updateOtUser } from "@/lib/api/admin-ot-security";
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

const pendingUser: OtUserItem = {
  id: "u-3",
  fullName: "Invitado Pendiente",
  email: "pendiente@transito.gov.co",
  role: null,
  roleCode: null,
  roleId: null,
  status: "pending",
  createdAt: null,
  isSuspended: false,
  rowVersion: 0,
};

describe("OtUsersSection — botón Editar (#10622)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC4: no muestra el botón Editar para un usuario con estado Pendiente", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [pendingUser] });
    renderSection();

    await screen.findByText("Invitado Pendiente");
    expect(
      screen.queryByRole("button", { name: /editar usuario invitado pendiente/i }),
    ).not.toBeInTheDocument();
  });

  it("AC1: abre el modal prellenado con nombre y correo al hacer clic en Editar", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("Laura García");
    await user.click(screen.getByRole("button", { name: /editar usuario laura garcía/i }));

    expect(await screen.findByLabelText(/nombre completo/i)).toHaveValue("Laura García");
    expect(screen.getByLabelText(/correo electrónico/i)).toHaveValue("laura@transito.gov.co");
  });

  it("guarda los cambios llamando a updateOtUser con el scope OT y recarga el listado", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    vi.mocked(updateOtUser).mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("Laura García");
    await user.click(screen.getByRole("button", { name: /editar usuario laura garcía/i }));

    const nameInput = await screen.findByLabelText(/nombre completo/i);
    await user.clear(nameInput);
    await user.type(nameInput, "Laura García Ruiz");
    await user.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() =>
      expect(updateOtUser).toHaveBeenCalledWith(
        "u-1",
        {
          // El correo es la credencial de acceso: se muestra en solo lectura y no viaja en el PATCH.
          displayName: "Laura García Ruiz",
          rowVersion: 7,
        },
        { transitOfficeId: "ot-1" },
      ),
    );
    await waitFor(() => expect(fetchOtUsers).toHaveBeenCalledTimes(2));
  });
});
