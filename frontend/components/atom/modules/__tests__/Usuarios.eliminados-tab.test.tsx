// HU #10623 (AC3) — Pestaña "Eliminados", exclusiva de SuperAdmin.
//
// BLOQUEANTE reportado: GET /api/v1/security/users excluye incondicionalmente a los usuarios
// con `deletedAt` (ver SecurityEndpoints.cs) y no expone ningún parámetro para listar SOLO
// eliminados (p. ej. `?onlyDeleted=true`). Esta prueba cubre lo que SÍ es verificable con el
// contrato actual: la pestaña es visible solo para SuperAdmin (AC4) y muestra el aviso de
// bloqueo en vez de datos inventados. Cuando el backend agregue soporte, este test debe
// actualizarse para cubrir el listado real + restauración (RestoreUserDialog ya implementado).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";
import { getUsers } from "@/lib/api/security";

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
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficeTenants: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

describe("Usuarios — pestaña Eliminados (#10623, SuperAdmin)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getUsers).mockResolvedValue([]);
  });

  it("AC3/AC4: SuperAdmin ve la pestaña Eliminados", async () => {
    render(<Usuarios />);
    expect(await screen.findByRole("button", { name: /^eliminados$/i })).toBeInTheDocument();
  });

  it("AC3: al abrir la pestaña, informa el bloqueo de backend en vez de datos inventados", async () => {
    const user = userEvent.setup();
    render(<Usuarios />);

    await user.click(await screen.findByRole("button", { name: /^eliminados$/i }));

    expect(
      screen.getByText(/usuarios eliminados de cualquier compañía u organismo/i),
    ).toBeInTheDocument();
    expect(screen.getByText(/GET \/api\/v1\/security\/users/i)).toBeInTheDocument();
  });

  it("AC2 (SuperAdmin): sin botón Eliminar en la tabla de Usuarios para SuperAdmin (patrón existente de solo-lectura cross-tenant)", async () => {
    render(<Usuarios />);
    await screen.findByRole("button", { name: /^eliminados$/i });
    expect(screen.queryByRole("button", { name: /eliminar usuario/i })).not.toBeInTheDocument();
  });
});
