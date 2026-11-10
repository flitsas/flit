// HU #14427 (Feature #14420) — «Importar Excel» junto a «Invitar usuario» en el módulo Usuarios.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";

const session = vi.hoisted(() => ({ isSuperAdmin: false, isAdminCompany: true }));

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: session.isSuperAdmin,
    isAdminCompany: session.isAdminCompany,
    isOtAdmin: false,
    permissions: [],
    tenantId: "tenant-1",
    userId: "user-1",
    roleId: "role-1",
    roleCode: session.isSuperAdmin ? "SuperAdmin" : session.isAdminCompany ? "AdminCompany" : "Radicador",
  }),
}));

vi.mock("@/lib/api/security", () => ({
  getUsers: vi.fn().mockResolvedValue([]),
  getRoles: vi.fn().mockResolvedValue([]),
  createInvitation: vi.fn(),
  assignRole: vi.fn(),
  blockUser: vi.fn(),
  unblockUser: vi.fn(),
  updateUser: vi.fn(),
  deleteUser: vi.fn(),
  restoreUser: vi.fn(),
  resendInvitation: vi.fn(),
}));
vi.mock("@/lib/api/admin-companies", () => ({
  fetchAllCompanies: vi.fn().mockResolvedValue([{ id: "company-1", razonSocial: "Compañía Demo" }]),
}));
vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchAllTransitOfficeTenants: vi.fn().mockResolvedValue([]),
}));
vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: { listRoles: vi.fn().mockResolvedValue([]) },
}));

import { getUsers } from "@/lib/api/security";

beforeEach(() => {
  vi.clearAllMocks();
  session.isSuperAdmin = false;
  session.isAdminCompany = true;
});

describe("Usuarios — botón Importar Excel", () => {
  it("AC1: AdminCompany lo ve junto a Invitar y abre el asistente con su compañía fija", async () => {
    const user = userEvent.setup();
    render(<Usuarios />);

    expect(await screen.findByRole("button", { name: /invitar usuario/i })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /importar excel/i }));

    expect(await screen.findByRole("dialog", { name: /importar usuarios desde excel/i })).toBeInTheDocument();
    expect(screen.getByTestId("bulk-invite-context")).toHaveTextContent(/tu compañía/i);
    expect(screen.getByRole("button", { name: /descargar plantilla/i })).toBeEnabled();
  });

  it("AC1: SuperAdmin abre el asistente con selector de Perfil y destino", async () => {
    session.isSuperAdmin = true;
    session.isAdminCompany = false;
    const user = userEvent.setup();
    render(<Usuarios />);

    await user.click(await screen.findByRole("button", { name: /importar excel/i }));

    expect(await screen.findByTestId("bulk-invite-context-picker")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /descargar plantilla/i })).toBeDisabled();
  });

  it("AC2: sin permiso de invitar el botón no aparece", async () => {
    session.isAdminCompany = false;
    render(<Usuarios />);

    await screen.findByText(/gestiona el acceso/i);
    expect(screen.queryByRole("button", { name: /importar excel/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /invitar usuario/i })).not.toBeInTheDocument();
  });

  it("no cambia «Invitar usuario»: sigue abriendo su propio modal", async () => {
    const user = userEvent.setup();
    render(<Usuarios />);

    await user.click(await screen.findByRole("button", { name: /invitar usuario/i }));

    expect(screen.getByRole("heading", { name: /invitar usuario/i })).toBeInTheDocument();
    expect(screen.queryByRole("dialog", { name: /importar usuarios/i })).not.toBeInTheDocument();
  });

  it("cerrar el asistente lo desmonta y no recarga la lista si no se invitó a nadie", async () => {
    const user = userEvent.setup();
    render(<Usuarios />);
    await user.click(await screen.findByRole("button", { name: /importar excel/i }));
    const callsBefore = vi.mocked(getUsers).mock.calls.length;

    await user.click(screen.getByRole("button", { name: "Cancelar" }));

    expect(screen.queryByRole("dialog", { name: /importar usuarios/i })).not.toBeInTheDocument();
    expect(vi.mocked(getUsers).mock.calls.length).toBe(callsBefore);
  });
});
