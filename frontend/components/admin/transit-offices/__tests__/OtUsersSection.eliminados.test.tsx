// HU #10623 (AC3/AC4) — Toggle "Ver eliminados" en la pestaña "Usuarios" del hub OT,
// exclusivo de SuperAdmin (este hub no tiene tabs, a diferencia de Usuarios.tsx).
//
// BLOQUEANTE reportado: GET /api/v1/admin/ot/users (fetchOtUsers) excluye incondicionalmente a
// los usuarios con `deletedAt` y no expone un parámetro para listar SOLO eliminados. Esta prueba
// cubre lo verificable con el contrato actual: el toggle es visible solo para SuperAdmin y
// muestra el aviso de bloqueo en vez de datos inventados.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import { fetchOtUsers } from "@/lib/api/admin-ot-security";
import type { OtUserItem } from "@/lib/api/admin-ot-security";

vi.mock("@/lib/api/admin-ot-security", () => ({
  fetchOtUsers: vi.fn(),
  inviteOtUser: vi.fn(),
  suspendOtUser: vi.fn(),
  unsuspendOtUser: vi.fn(),
  updateOtUser: vi.fn(),
  deleteOtUser: vi.fn(),
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

describe("OtUsersSection — toggle Ver eliminados (#10623, SuperAdmin)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
  });

  it("AC3/AC4: SuperAdmin ve el botón 'Ver eliminados'", async () => {
    renderSection();
    expect(await screen.findByRole("button", { name: /ver eliminados/i })).toBeInTheDocument();
  });

  it("AC3: al activarlo, informa el bloqueo de backend en vez de datos inventados", async () => {
    const user = userEvent.setup();
    renderSection();

    await user.click(await screen.findByRole("button", { name: /ver eliminados/i }));

    expect(
      screen.getByText(/usuarios eliminados de este organismo de tránsito/i),
    ).toBeInTheDocument();
    expect(screen.getByText(/GET \/api\/v1\/admin\/ot\/users/i)).toBeInTheDocument();
    // La tabla de usuarios activos se oculta mientras se muestra el bloqueo.
    expect(screen.queryByText("Laura García")).not.toBeInTheDocument();
  });
});
