// HU #13812 — pruebas de unicidad de correo en la invitación (archivo aparte para no reescribir las existentes).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

vi.mock("@/lib/api/security", async (orig) => ({
  ...(await orig<typeof import("@/lib/api/security")>()),
  createInvitation: vi.fn(),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchAllCompanies: vi.fn().mockResolvedValue([{ id: "company-1", razonSocial: "Compañía Demo" }]),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchAllTransitOfficeTenants: vi.fn().mockResolvedValue([
    { id: "ot-tenant-1", legalName: "Secretaría Demo", transitOfficeCode: "OT-DEMO" },
  ]),
}));

const listRoles = vi.fn();
vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: { listRoles: (t: string) => listRoles(t) },
}));

import { createInvitation } from "@/lib/api/security";
import { InviteUserModal } from "../InviteUserModal";

const companyRoles = [
  { id: "role-super", code: "SuperAdmin", name: "Super Administrador", description: null, isSystem: true, isActive: true, permissionCount: 0, createdAt: "" },
  { id: "role-admin", code: "AdminCompany", name: "Administrador de Compañía", description: null, isSystem: true, isActive: true, permissionCount: 0, createdAt: "" },
  { id: "role-radicador", code: "Radicador", name: "Radicador", description: null, isSystem: false, isActive: true, permissionCount: 0, createdAt: "" },
];

const otRoles = [
  { id: "role-ot", code: "ot_admin", name: "Administrador OT", description: null, isSystem: true, isActive: true, permissionCount: 0, createdAt: "" },
  { id: "role-revisor", code: "revisor", name: "Revisor documental", description: null, isSystem: false, isActive: true, permissionCount: 0, createdAt: "" },
];

function renderModal(props: Partial<React.ComponentProps<typeof InviteUserModal>> = {}) {
  return render(
    <InviteUserModal
      onClose={vi.fn()}
      onSuccess={vi.fn()}
      isSuperAdmin
      roles={[]}
      rolesLoading={false}
      {...props}
    />,
  );
}


// HU #13812 (Feature #13808, Épica #13516) — el correo se envía normalizado y, ante el conflicto de
// correo, el campo queda marcado como inválido con el mensaje único.
// Uso de ejemplo: escribir " Nuevo@Flit.LOCAL " envía "nuevo@flit.local" a createInvitation.

describe("InviteUserModal — HU #13812 unicidad de correo", () => {
  beforeEach(() => {
    vi.mocked(createInvitation).mockReset().mockResolvedValue({
      invitationId: "inv-1",
      email: "nuevo@flit.local",
      emailSent: true,
    });
    listRoles.mockReset().mockImplementation((t: string) =>
      Promise.resolve(t === "TRANSIT_OFFICE" ? otRoles : companyRoles),
    );
  });

  async function invitarFlit(user: ReturnType<typeof userEvent.setup>, correo: string) {
    renderModal();
    await user.click(await screen.findByRole("radio", { name: /^FLIT/i }));
    await user.type(screen.getByLabelText(/nombre completo/i), "Nuevo Usuario");
    await user.type(screen.getByLabelText(/correo electrónico/i), correo);
    await user.click(screen.getByRole("button", { name: /enviar instrucciones/i }));
  }

  it("envía el correo sin espacios y en minúsculas", async () => {
    const user = userEvent.setup();
    await invitarFlit(user, " Nuevo@Flit.LOCAL ");

    await waitFor(() =>
      expect(createInvitation).toHaveBeenCalledWith("nuevo@flit.local", "Nuevo Usuario", ["role-super"], undefined),
    );
  });

  it("ante el conflicto de correo marca el campo como inválido y muestra el mensaje único", async () => {
    vi.mocked(createInvitation).mockRejectedValue(
      Object.assign(new Error("conflict"), { status: 409, body: { code: "EMAIL_ALREADY_IN_USE" } }),
    );
    const user = userEvent.setup();
    await invitarFlit(user, "nuevo@flit.local");

    const alerta = await screen.findByText("El usuario ya se encuentra previamente creado");
    const campo = screen.getByLabelText(/correo electrónico/i);
    expect(campo).toHaveAttribute("aria-invalid", "true");
    expect(campo).toHaveAttribute("aria-describedby", alerta.id);
  });

  it("al corregir el correo el campo deja de estar inválido", async () => {
    vi.mocked(createInvitation).mockRejectedValue(
      Object.assign(new Error("conflict"), { status: 409, body: { code: "EMAIL_ALREADY_IN_USE" } }),
    );
    const user = userEvent.setup();
    await invitarFlit(user, "nuevo@flit.local");
    await screen.findByText("El usuario ya se encuentra previamente creado");

    await user.type(screen.getByLabelText(/correo electrónico/i), "x");

    expect(screen.getByLabelText(/correo electrónico/i)).not.toHaveAttribute("aria-invalid");
    expect(screen.queryByText("El usuario ya se encuentra previamente creado")).toBeNull();
  });
});
