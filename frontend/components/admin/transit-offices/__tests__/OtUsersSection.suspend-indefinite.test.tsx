// Ajuste QA (ejecución manual del flujo completo del Feature #10618): el diálogo "Suspender
// usuario" del hub OT siempre exigía una fecha de fin, sin ofrecer la opción de desactivación
// indefinida del AC1 de la HU #10619 (el backend ya soporta EndsAt nulo). Mismo ajuste que
// Usuarios.suspend-indefinite.test.tsx, aplicado a la sección OT.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import { fetchOtUsers, suspendOtUser } from "@/lib/api/admin-ot-security";
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

// Bloquear/desactivar es EXCLUSIVO de SuperAdmin (el ot_admin ya no puede): se renderiza como SuperAdmin.
vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: true,
    isAdminCompany: false,
    isOtAdmin: false,
    permissions: [],
    tenantId: "ot-tenant-1",
    userId: "u-self",
    roleId: "role-super",
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

describe("OtUsersSection — suspender usuario (ajuste QA: desactivación indefinida, HU #10619 AC1)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("por defecto exige fecha de fin (comportamiento existente sin cambios)", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("Laura García");
    await user.click(screen.getByRole("button", { name: /suspender temporalmente a laura garcía/i }));

    expect(await screen.findByLabelText(/suspendido hasta/i)).toBeInTheDocument();
  });

  it("al marcar 'Desactivar indefinidamente' oculta la fecha de fin y llama a suspendOtUser con endsAt=null", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    vi.mocked(suspendOtUser).mockResolvedValue({ id: "u-1" });
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("Laura García");
    await user.click(screen.getByRole("button", { name: /suspender temporalmente a laura garcía/i }));
    await user.type(await screen.findByLabelText(/motivo/i), "Desactivación indefinida de prueba");
    await user.click(screen.getByRole("checkbox", { name: /desactivar indefinidamente/i }));

    expect(screen.queryByLabelText(/suspendido hasta/i)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /^desactivar usuario$/i }));

    await waitFor(() =>
      expect(suspendOtUser).toHaveBeenCalledWith(
        "u-1",
        { reason: "Desactivación indefinida de prueba", endsAt: null },
        { transitOfficeId: "ot-1" },
      ),
    );
  });
});
