// HU #10623 — Botón "Eliminar" en la pestaña "Usuarios" del hub OT. AC1: confirmación clara
// (solo SuperAdmin restaura). AC2: sin auto-eliminación.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import { fetchOtUsers, deleteOtUser } from "@/lib/api/admin-ot-security";
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

const selfUser: OtUserItem = {
  id: "u-self",
  fullName: "Yo Mismo",
  email: "yo@transito.gov.co",
  role: "Admin OT",
  roleCode: "ot_admin",
  roleId: "role-1",
  status: "active",
  createdAt: "2026-06-23T10:00:00Z",
  isSuspended: false,
  rowVersion: 1,
};

describe("OtUsersSection — botón Eliminar (#10623)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC2: no muestra el botón Eliminar sobre la propia fila del usuario autenticado", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [selfUser, activeUser] });
    renderSection();

    await screen.findByText("Laura García");
    expect(
      screen.queryByRole("button", { name: /eliminar usuario yo mismo/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /eliminar usuario laura garcía/i }),
    ).toBeInTheDocument();
  });

  it("AC1: abre la confirmación con el aviso de que solo un Super Admin puede restaurar", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("Laura García");
    await user.click(screen.getByRole("button", { name: /eliminar usuario laura garcía/i }));

    // El texto está partido por un <strong>, se busca por el textContent del párrafo completo.
    expect(
      await screen.findByText(
        (_, node) => node?.tagName === "P" && /solo un.*super admin.*puede restaurarlo/i.test(node.textContent ?? ""),
      ),
    ).toBeInTheDocument();
  });

  it("confirma la eliminación llamando a deleteOtUser con el scope OT y recarga el listado", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    vi.mocked(deleteOtUser).mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("Laura García");
    await user.click(screen.getByRole("button", { name: /eliminar usuario laura garcía/i }));
    await user.click(screen.getByRole("button", { name: /^eliminar usuario$/i }));

    await waitFor(() =>
      expect(deleteOtUser).toHaveBeenCalledWith(
        "u-1",
        { rowVersion: 7 },
        { transitOfficeId: "ot-1" },
      ),
    );
    await waitFor(() => expect(fetchOtUsers).toHaveBeenCalledTimes(2));
  });

  it("AC4: ot_admin no ve el botón 'Ver eliminados' (exclusivo de SuperAdmin)", async () => {
    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [activeUser] });
    renderSection();

    await screen.findByText("Laura García");
    expect(screen.queryByRole("button", { name: /ver eliminados/i })).not.toBeInTheDocument();
  });
});
