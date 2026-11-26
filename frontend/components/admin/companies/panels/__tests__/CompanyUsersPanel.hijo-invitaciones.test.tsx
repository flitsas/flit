// Bug #14714 — la cabeza (AdminCompany) gestiona a su hijo desde la ficha de red. El listado del hijo
// llega por GET …/children/{id}/users: antes solo traía usuarios ya activos y una invitación pendiente
// dejaba la tabla en «No hay usuarios en esta compañía». Ahora las invitaciones llegan con su estado real.
import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { CompanyUsersPanel } from "../CompanyUsersPanel";
import { fetchChildCompanyUsers } from "@/lib/api/admin-companies";

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: false,
    isAdminCompany: true,
    isOtAdmin: false,
    permissions: [],
    tenantId: "head-tenant",
    userId: "head-user",
    roleId: "role-ac",
    roleCode: "AdminCompany",
  }),
}));

vi.mock("@/lib/api/security", () => ({
  getUsers: vi.fn().mockResolvedValue([]),
  getRoles: vi.fn().mockResolvedValue([]),
  assignRole: vi.fn(),
  updateUser: vi.fn(),
}));

vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: { listRoles: vi.fn().mockResolvedValue([]) },
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchChildCompanyUsers: vi.fn(),
  inviteChildCompanyUser: vi.fn(),
}));

describe("CompanyUsersPanel — hijo de red con invitaciones (#14714)", () => {
  it("muestra la invitación pendiente del hijo en vez de «No hay usuarios»", async () => {
    vi.mocked(fetchChildCompanyUsers).mockResolvedValue([
      {
        userId: "inv-1",
        displayName: "Admin del hijo",
        email: "admin.hijo@flit.local",
        roleCode: "AdminCompany",
        roleId: "role-ac",
        status: "pending",
      },
    ]);

    render(<CompanyUsersPanel tenantId="child-tenant" networkHeadId="head-tenant" />);

    expect(await screen.findByText("Admin del hijo")).toBeInTheDocument();
    expect(screen.getByText("admin.hijo@flit.local")).toBeInTheDocument();
    const fila = screen.getByText("Admin del hijo").closest("tr, [role=row]") as HTMLElement;
    expect(within(fila).getByText(/pendiente/i)).toBeInTheDocument();
    expect(screen.queryByText(/no hay usuarios en esta compañía/i)).not.toBeInTheDocument();
    expect(fetchChildCompanyUsers).toHaveBeenCalledWith("head-tenant", "child-tenant");
  });
});
