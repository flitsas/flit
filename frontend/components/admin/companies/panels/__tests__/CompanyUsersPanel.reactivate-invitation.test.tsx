// HU #11552 / ADR-0048 — tercera pantalla consumidora de UsersTable. AC1: la fila cancelada se
// ve con su badge. AC4: no ofrece "Ver historial"/"Editar" sobre una fila cuyo `id` es un
// invitationId, no un userId (antes `status === "pending"` no excluía "cancelled" — dejaba pasar
// el guarda de `actionsFor`).
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { CompanyUsersPanel } from "../CompanyUsersPanel";
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
  assignRole: vi.fn(),
  updateUser: vi.fn(),
}));

vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: {
    listRoles: vi.fn().mockResolvedValue([]),
  },
}));

describe("CompanyUsersPanel — invitación cancelada (#11552)", () => {
  it("AC1/AC4: muestra el badge 'Cancelada' y no ofrece Editar ni Ver historial sobre esa fila", async () => {
    vi.mocked(getUsers).mockResolvedValueOnce([
      {
        id: "invitation-company-cancelled",
        fullName: "Iván Cortés",
        email: "ivan@flit.local",
        role: null,
        roleCode: null,
        roleId: null,
        status: "cancelled",
        createdAt: "2026-08-01T10:00:00Z",
        isSuspended: false,
        tenantId: "tenant-1",
        tenantType: "COMPANY",
        profile: null,
        rowVersion: 1,
      },
    ]);

    render(<CompanyUsersPanel tenantId="tenant-1" />);

    await screen.findByText("Iván Cortés");
    expect(screen.getByRole("status", { name: /^Estado: Cancelada$/i })).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /editar usuario iván cortés/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /ver historial de iván cortés/i }),
    ).not.toBeInTheDocument();
  });
});
