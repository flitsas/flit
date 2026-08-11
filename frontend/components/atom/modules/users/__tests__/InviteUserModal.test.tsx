// Alta de usuario con selector de PERFIL y de ROL (context/usuarios-contex.md). Antes el
// SuperAdmin no podía elegir rol —el backend lo forzaba según el tenant destino— y no existía
// forma de crear un usuario de perfil FLIT sin compañía ni organismo.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

vi.mock("@/lib/api/security", async (orig) => ({
  ...(await orig<typeof import("@/lib/api/security")>()),
  createInvitation: vi.fn(),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: vi.fn().mockResolvedValue({
    data: [{ id: "company-1", razonSocial: "Compañía Demo" }],
    totalCount: 1,
    page: 1,
    pageSize: 200,
  }),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficeTenants: vi.fn().mockResolvedValue({
    data: [{ id: "ot-tenant-1", legalName: "Secretaría Demo", transitOfficeCode: "OT-DEMO" }],
    totalCount: 1,
    page: 1,
    pageSize: 200,
  }),
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

async function fillIdentity(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/nombre completo/i), "Nuevo Usuario");
  await user.type(screen.getByLabelText(/correo electrónico/i), "nuevo@flit.local");
}

describe("InviteUserModal — SuperAdmin", () => {
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

  it("ofrece los roles de compañía y envía el seleccionado", async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(await screen.findByRole("radio", { name: /Gestor/i }));
    // El destino es un combobox con buscador: se abre y se elige la opción.
    await user.click(await screen.findByLabelText(/compañía destino/i));
    await user.click(await screen.findByRole("option", { name: /Compañía Demo/ }));
    await fillIdentity(user);
    await user.click(await screen.findByRole("radio", { name: "Radicador" }));
    await user.click(screen.getByRole("button", { name: /enviar instrucciones/i }));

    await waitFor(() =>
      expect(createInvitation).toHaveBeenCalledWith(
        "nuevo@flit.local",
        "Nuevo Usuario",
        ["role-radicador"],
        "company-1",
      ),
    );
  });

  it("no ofrece el rol SuperAdmin dentro de una compañía", async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(await screen.findByRole("radio", { name: /Gestor/i }));

    expect(await screen.findByRole("radio", { name: "Radicador" })).toBeInTheDocument();
    expect(screen.queryByRole("radio", { name: "Super Administrador" })).not.toBeInTheDocument();
  });

  it("ofrece los roles del organismo cuando el perfil es OT", async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(await screen.findByRole("radio", { name: /Organismo de Tránsito/i }));

    expect(await screen.findByRole("radio", { name: "Revisor documental" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Administrador OT" })).toBeInTheDocument();
    expect(listRoles).toHaveBeenCalledWith("TRANSIT_OFFICE");
  });

  it("crea un perfil FLIT sin compañía ni organismo destino", async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(await screen.findByRole("radio", { name: /^FLIT/i }));
    await fillIdentity(user);
    await user.click(screen.getByRole("button", { name: /enviar instrucciones/i }));

    await waitFor(() =>
      expect(createInvitation).toHaveBeenCalledWith(
        "nuevo@flit.local",
        "Nuevo Usuario",
        ["role-super"],
        // Sin tenant destino: el perfil FLIT no pertenece a ninguna compañía ni organismo.
        undefined,
      ),
    );
  });

  it("con alcance fijo no pide destino pero sí deja elegir rol", async () => {
    const user = userEvent.setup();
    renderModal({ fixedTarget: { tenantId: "company-1", profile: "GESTOR" } });

    expect(screen.queryByLabelText(/compañía destino/i)).not.toBeInTheDocument();
    await user.click(await screen.findByRole("radio", { name: "Radicador" }));
    await fillIdentity(user);
    await user.click(screen.getByRole("button", { name: /enviar instrucciones/i }));

    await waitFor(() =>
      expect(createInvitation).toHaveBeenCalledWith(
        "nuevo@flit.local",
        "Nuevo Usuario",
        ["role-radicador"],
        "company-1",
      ),
    );
  });

  it("traduce el error de rol incoherente con el destino", async () => {
    vi.mocked(createInvitation).mockRejectedValue(
      Object.assign(new Error("conflict"), {
        status: 409,
        body: { code: "ROLE_TARGET_ENTITY_TYPE_MISMATCH" },
      }),
    );
    const user = userEvent.setup();
    renderModal();

    await user.click(await screen.findByRole("radio", { name: /Gestor/i }));
    // El destino es un combobox con buscador: se abre y se elige la opción.
    await user.click(await screen.findByLabelText(/compañía destino/i));
    await user.click(await screen.findByRole("option", { name: /Compañía Demo/ }));
    await fillIdentity(user);
    await user.click(await screen.findByRole("radio", { name: "Radicador" }));
    await user.click(screen.getByRole("button", { name: /enviar instrucciones/i }));

    expect(await screen.findByText(/no aplica al destino elegido/i)).toBeInTheDocument();
  });
});

describe("InviteUserModal — AdminCompany", () => {
  beforeEach(() => {
    vi.mocked(createInvitation).mockReset().mockResolvedValue({
      invitationId: "inv-2",
      email: "nuevo@flit.local",
      emailSent: true,
    });
  });

  it("usa los roles de su propio tenant y exige al menos uno", async () => {
    const user = userEvent.setup();
    renderModal({
      isSuperAdmin: false,
      roles: [
        { id: "role-radicador", code: "Radicador", name: "Radicador", description: null, isSystem: false, permissionCount: 0, createdAt: "" },
      ],
    });

    // Sin perfil que elegir: el suyo lo fija su tenant.
    expect(screen.queryByRole("radio", { name: /Gestor/i })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /enviar instrucciones/i })).toBeDisabled();

    await fillIdentity(user);
    await user.click(screen.getByRole("radio", { name: "Radicador" }));
    await user.click(screen.getByRole("button", { name: /enviar instrucciones/i }));

    await waitFor(() =>
      expect(createInvitation).toHaveBeenCalledWith(
        "nuevo@flit.local",
        "Nuevo Usuario",
        ["role-radicador"],
        undefined,
      ),
    );
  });
});
