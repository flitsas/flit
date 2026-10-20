// HU #13657 (Feature #13104 F-D2) — Panel SA: ficha de compañía. Al cambiar el rol de un usuario con
// permisos individuales se avisa cuántos se eliminarán; el Super Admin consulta con el tenant del usuario.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyUsersPanel } from "../CompanyUsersPanel";
import { assignRole, getUsers } from "@/lib/api/security";
import { superadminClient } from "@/lib/api/superadmin-client";
import { countUserOverrides } from "@/lib/api/permission-overrides";

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: true, isAdminCompany: false, isOtAdmin: false, permissions: [], tenantId: "sa-tenant",
    userId: "sa-user", roleId: "role-sa", roleCode: "SuperAdmin",
  }),
}));
vi.mock("@/lib/api/security", () => ({ getUsers: vi.fn(), assignRole: vi.fn(), updateUser: vi.fn(), getRoles: vi.fn() }));
vi.mock("@/lib/api/superadmin-client", () => ({ superadminClient: { listRoles: vi.fn() } }));
vi.mock("@/lib/api/permission-overrides", () => ({ countUserOverrides: vi.fn() }));

const role = (id: string, name: string) => ({
  id, code: id, name, description: null, isSystem: true, permissionCount: 1, createdAt: "", isActive: true, productCode: "tramites",
});

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getUsers).mockResolvedValue([
    {
      id: "u-1", fullName: "Hugo Vélez", email: "hugo@flit.local", role: "Radicador", roleCode: "radicador", roleId: "r-rad",
      status: "active", createdAt: "2026-08-01T10:00:00Z", isSuspended: false, tenantId: "tenant-1", tenantType: "COMPANY",
      profile: "GESTOR", rowVersion: 1,
    },
  ]);
  vi.mocked(superadminClient.listRoles).mockResolvedValue([role("r-rad", "Radicador"), role("r-ges", "Gestor")] as never);
});

async function pickRole() {
  render(<CompanyUsersPanel tenantId="tenant-1" />);
  await userEvent.click(await screen.findByRole("button", { name: /editar usuario hugo vélez/i }));
  const dialog = await screen.findByRole("dialog");
  const select = await within(dialog).findByRole("combobox", { name: "Cambiar rol del usuario" });
  await waitFor(() => expect(select).toBeEnabled());
  await userEvent.selectOptions(select, "r-ges");
}

describe("CompanyUsersPanel — cambio de rol (#13657)", () => {
  it("AC1: con 1 permiso individual avisa (singular) con el tenant del usuario y cambia solo al confirmar", async () => {
    vi.mocked(countUserOverrides).mockResolvedValue({ count: 1 });
    vi.mocked(assignRole).mockResolvedValue(undefined);
    await pickRole();
    expect(
      await screen.findByText("Este usuario tiene 1 permiso individual que se eliminará al cambiar su rol."),
    ).toBeInTheDocument();
    expect(countUserOverrides).toHaveBeenCalledWith("u-1", "tramites", "tenant-1", "r-ges");
    expect(assignRole).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole("button", { name: "Cambiar rol" }));
    await waitFor(() => expect(assignRole).toHaveBeenCalledWith("u-1", "r-ges"));
  });

  it("AC3: cancelar deja el rol sin cambios", async () => {
    vi.mocked(countUserOverrides).mockResolvedValue({ count: 3 });
    await pickRole();
    await screen.findByText(/3 permisos individuales/);
    await userEvent.click(screen.getByRole("button", { name: "Mantener rol actual" }));
    expect(assignRole).not.toHaveBeenCalled();
    expect(screen.getByRole("combobox", { name: "Cambiar rol del usuario" })).toHaveValue("r-rad");
  });
});
