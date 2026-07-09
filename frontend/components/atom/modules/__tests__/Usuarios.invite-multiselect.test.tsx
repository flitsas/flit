// HU #10510 — Selección múltiple de roles al invitar usuarios. AdminCompany/OtAdmin ahora
// pueden marcar VARIOS roles (checklist, no <select> single-value) al invitar. AC1: se pueden
// marcar varios roles disponibles, incluidos los de sistema. AC2: el payload enviado a
// createInvitation trae `roleIds: string[]` con todos los marcados. AC3: sin ningún rol
// marcado, el envío queda bloqueado y se muestra un mensaje de ayuda.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Usuarios } from "../Usuarios";
import { createInvitation } from "@/lib/api/security";

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: false,
    isAdminCompany: true,
    isOtAdmin: false,
    permissions: [],
    tenantId: "tenant-1",
    userId: "user-1",
    roleId: "role-supervisor",
    roleCode: "AdminCompany",
  }),
}));

const rolesFixture = vi.hoisted(() => [
  {
    id: "role-supervisor",
    code: "SUPERVISOR",
    name: "Supervisor",
    description: "Rol de la empresa",
    isSystem: false,
    permissionCount: 3,
    createdAt: "2026-01-01T00:00:00Z",
  },
  {
    id: "role-admin-company",
    code: "AdminCompany",
    name: "Administrador de Compañía",
    description: "Rol de sistema",
    isSystem: true,
    permissionCount: 20,
    createdAt: "2026-01-01T00:00:00Z",
  },
]);

vi.mock("@/lib/api/security", () => ({
  getUsers: vi.fn().mockResolvedValue([]),
  getRoles: vi.fn().mockResolvedValue(rolesFixture),
  createInvitation: vi.fn().mockResolvedValue({
    invitationId: "inv-1",
    email: "nuevo@flit.local",
    emailSent: true,
  }),
  assignRole: vi.fn(),
  blockUser: vi.fn(),
  unblockUser: vi.fn(),
  updateUser: vi.fn(),
  deleteUser: vi.fn(),
  restoreUser: vi.fn(),
  resendInvitation: vi.fn(),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficeTenants: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 200 }),
}));

beforeEach(() => {
  vi.clearAllMocks();
});

async function openInviteModal() {
  const user = userEvent.setup();
  render(<Usuarios />);
  await user.click(await screen.findByRole("button", { name: /invitar usuario/i }));
  return user;
}

describe("Usuarios — InviteModal — selección múltiple de roles (HU #10510)", () => {
  it("AC1: muestra un checkbox por cada rol disponible, incluyendo roles de sistema, y permite marcar varios", async () => {
    const user = await openInviteModal();

    const supervisorCheckbox = await screen.findByRole("checkbox", { name: /supervisor/i });
    const adminCompanyCheckbox = await screen.findByRole("checkbox", { name: /administrador de compañía/i });

    expect(supervisorCheckbox).not.toBeChecked();
    expect(adminCompanyCheckbox).not.toBeChecked();

    await user.click(supervisorCheckbox);
    await user.click(adminCompanyCheckbox);

    expect(supervisorCheckbox).toBeChecked();
    expect(adminCompanyCheckbox).toBeChecked();
  });

  it("AC3: sin ningún rol marcado, el botón de enviar está deshabilitado y se muestra un mensaje de ayuda", async () => {
    await openInviteModal();

    expect(screen.getByText(/selecciona al menos un rol/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /enviar instrucciones/i })).toBeDisabled();
    expect(createInvitation).not.toHaveBeenCalled();
  });

  it("AC2: al enviar con 2 roles marcados, llama a createInvitation con roleIds: string[] con ambos ids", async () => {
    const user = await openInviteModal();

    await user.type(await screen.findByLabelText(/nombre completo/i), "Laura García");
    await user.type(screen.getByLabelText(/correo electrónico/i), "laura@empresa.com");

    await user.click(await screen.findByRole("checkbox", { name: /supervisor/i }));
    await user.click(screen.getByRole("checkbox", { name: /administrador de compañía/i }));

    const submitButton = screen.getByRole("button", { name: /enviar instrucciones/i });
    expect(submitButton).toBeEnabled();
    await user.click(submitButton);

    expect(createInvitation).toHaveBeenCalledWith(
      "laura@empresa.com",
      "Laura García",
      expect.arrayContaining(["role-supervisor", "role-admin-company"]),
      undefined,
    );
    const roleIdsArg = vi.mocked(createInvitation).mock.calls[0][2];
    expect(roleIdsArg).toHaveLength(2);

    expect(await screen.findByText(/se enviaron instrucciones de activación/i)).toBeInTheDocument();
  });
});
