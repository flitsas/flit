// Alcance de clientes por usuario del OT: Admin OT y Super Admin eligen «Todos los clientes» o «Clientes
// específicos» al editar e invitar; el Gestor OT no ve la sección y nadie edita su propio alcance.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import {
  fetchOtClientCompanies,
  fetchOtUserClientScope,
  fetchOtUsers,
  inviteOtUser,
  setOtUserClientScope,
  updateOtUser,
} from "@/lib/api/admin-ot-security";
import type { OtUserItem } from "@/lib/api/admin-ot-security";
import { ApiError } from "@/lib/api/types";
import { getRoles } from "@/lib/api/security";
import { chooseRowAction } from "@/components/atom/__tests__/rowActionsMenu.helpers";

vi.mock("@/lib/api/admin-ot-security", () => ({
  fetchOtUsers: vi.fn(),
  inviteOtUser: vi.fn(),
  suspendOtUser: vi.fn(),
  unsuspendOtUser: vi.fn(),
  updateOtUser: vi.fn(),
  deleteOtUser: vi.fn(),
  resendOtInvitation: vi.fn(),
  cancelOtInvitation: vi.fn(),
  reactivateOtInvitation: vi.fn(),
  fetchOtClientCompanies: vi.fn(),
  fetchOtUserClientScope: vi.fn(),
  setOtUserClientScope: vi.fn(),
}));
vi.mock("@/lib/api/security", () => ({
  restoreUser: vi.fn(),
  assignRole: vi.fn(),
  getRoles: vi.fn(),
}));
vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: { getRole: vi.fn(), listRoles: vi.fn() },
}));
vi.mock("@/lib/api/permission-overrides", () => ({
  countUserOverrides: vi.fn().mockResolvedValue({ count: 0 }),
}));

const session = vi.hoisted(() => ({ isOtAdmin: true }));
vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: false,
    isAdminCompany: false,
    isOtAdmin: session.isOtAdmin,
    permissions: [],
    tenantId: "ot-tenant-1",
    userId: "u-self",
    roleId: "r-adm",
    roleCode: session.isOtAdmin ? "ot_admin" : "gestor_tramites_ot",
  }),
}));

const mk = (over: Partial<OtUserItem>): OtUserItem => ({
  id: "u-1",
  fullName: "Gina Gestora",
  email: "gina@transito.gov.co",
  role: "Gestor OT",
  roleCode: "gestor_tramites_ot",
  roleId: "r-gestor",
  status: "active",
  createdAt: "2026-10-01T00:00:00Z",
  isSuspended: false,
  rowVersion: 3,
  ...over,
});
const gina = mk({});
const hugo = mk({
  id: "u-3",
  fullName: "Hugo Limitado",
  email: "hugo@transito.gov.co",
  clientScope: { mode: "especificos", clientCount: 2 },
});
const yo = mk({
  id: "u-self",
  fullName: "Yo Mismo",
  email: "yo@transito.gov.co",
  role: "Administrador OT",
  roleCode: "ot_admin",
  roleId: "r-adm",
});

const roles = [
  {
    id: "r-adm",
    code: "ot_admin",
    name: "Administrador OT",
    description: null,
    isSystem: true,
    permissionCount: 8,
    createdAt: "",
    productCode: "tramites",
  },
  {
    id: "r-gestor",
    code: "gestor_tramites_ot",
    name: "Gestor OT",
    description: null,
    isSystem: true,
    permissionCount: 2,
    createdAt: "",
    productCode: "tramites",
  },
];

const companies = [
  { tenantId: "c-1", name: "Transportes Alfa", nit: "900123456" },
  { tenantId: "c-2", name: "Logística Beta", nit: "800765432" },
  { tenantId: "c-3", name: "Carga Gamma", nit: null },
];

const renderSection = () =>
  render(
    <ToastProvider>
      <OtUsersSection transitOfficeId="ot-1" />
    </ToastProvider>,
  );

async function openEdit(name: string) {
  const user = userEvent.setup();
  renderSection();
  await screen.findByText(name);
  await chooseRowAction(user, name, `Editar usuario ${name}`);
  const dialog = await screen.findByRole("dialog");
  return { user, dialog };
}

beforeEach(() => {
  vi.clearAllMocks();
  session.isOtAdmin = true;
  vi.mocked(fetchOtUsers).mockResolvedValue({ data: [gina, hugo, yo] });
  vi.mocked(getRoles).mockResolvedValue(roles);
  vi.mocked(fetchOtClientCompanies).mockResolvedValue({ items: companies });
  vi.mocked(fetchOtUserClientScope).mockResolvedValue({ mode: "todos", clients: [] });
  vi.mocked(setOtUserClientScope).mockResolvedValue(undefined);
  vi.mocked(updateOtUser).mockResolvedValue(undefined);
  vi.mocked(inviteOtUser).mockResolvedValue({
    invitationId: "i-1",
    email: "x",
    emailSent: true,
  } as never);
});

describe("OtUsersSection — alcance de clientes", () => {
  it("la tabla muestra «Todos los clientes» o «N clientes» junto al rol", async () => {
    renderSection();
    await screen.findByText("Hugo Limitado");
    const etiquetas = screen.getAllByTestId("user-client-scope").map((e) => e.textContent);
    expect(etiquetas).toContain("2 clientes");
    expect(etiquetas).toContain("Todos los clientes");
  });

  it("editar: por defecto queda «Todos los clientes» y guardar no llama al PUT de alcance", async () => {
    const { user, dialog } = await openEdit("Gina Gestora");
    expect(await within(dialog).findByRole("radio", { name: "Todos los clientes" })).toBeChecked();
    expect(within(dialog).queryByTestId("ot-clientes")).not.toBeInTheDocument();

    await user.click(within(dialog).getByRole("button", { name: "Guardar cambios" }));
    await waitFor(() => expect(updateOtUser).toHaveBeenCalled());
    expect(setOtUserClientScope).not.toHaveBeenCalled();
  });

  it("editar: cambiar a clientes específicos y guardar llama al PUT con los ids", async () => {
    const { user, dialog } = await openEdit("Gina Gestora");
    await user.click(await within(dialog).findByRole("radio", { name: "Clientes específicos" }));
    await user.click(await within(dialog).findByRole("checkbox", { name: /Transportes Alfa/ }));
    await user.click(within(dialog).getByRole("checkbox", { name: /Logística Beta/ }));
    await user.click(within(dialog).getByRole("button", { name: "Guardar cambios" }));

    await waitFor(() =>
      expect(setOtUserClientScope).toHaveBeenCalledWith(
        "u-1",
        { mode: "especificos", clientTenantIds: ["c-1", "c-2"] },
        { transitOfficeId: "ot-1" },
      ),
    );
    await waitFor(() => expect(updateOtUser).toHaveBeenCalled());
  });

  it("editar: precarga el alcance vigente del usuario", async () => {
    vi.mocked(fetchOtUserClientScope).mockResolvedValue({
      mode: "especificos",
      clients: [{ tenantId: "c-3", name: "Carga Gamma" }],
    });
    const { dialog } = await openEdit("Hugo Limitado");
    expect(
      await within(dialog).findByRole("radio", { name: "Clientes específicos" }),
    ).toBeChecked();
    expect(within(dialog).getByRole("button", { name: "Quitar Carga Gamma" })).toBeInTheDocument();
  });

  it("editar: con «específicos» y sin clientes muestra la validación y no deja guardar", async () => {
    const { user, dialog } = await openEdit("Gina Gestora");
    await user.click(await within(dialog).findByRole("radio", { name: "Clientes específicos" }));

    expect(await within(dialog).findByText(/elige al menos un cliente/i)).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Guardar cambios" })).toBeDisabled();
    expect(setOtUserClientScope).not.toHaveBeenCalled();
  });

  it("editar: un error del servidor (CLIENT_SCOPE_INVALID_CLIENT) se muestra claro y no guarda el usuario", async () => {
    vi.mocked(setOtUserClientScope).mockRejectedValue(
      new ApiError(400, "Bad Request", { error: "CLIENT_SCOPE_INVALID_CLIENT" }),
    );
    const { user, dialog } = await openEdit("Gina Gestora");
    await user.click(await within(dialog).findByRole("radio", { name: "Clientes específicos" }));
    await user.click(await within(dialog).findByRole("checkbox", { name: /Transportes Alfa/ }));
    await user.click(within(dialog).getByRole("button", { name: "Guardar cambios" }));

    expect(
      await within(dialog).findByText(/ya no pertenece a este organismo/i),
    ).toBeInTheDocument();
    expect(updateOtUser).not.toHaveBeenCalled();
  });

  it("editar: error al cargar el alcance ofrece reintentar", async () => {
    vi.mocked(fetchOtUserClientScope).mockRejectedValueOnce(new Error("red"));
    const { user, dialog } = await openEdit("Gina Gestora");
    expect(await within(dialog).findByText(/no se pudo cargar el alcance/i)).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: "Reintentar" }));
    expect(await within(dialog).findByRole("radio", { name: "Todos los clientes" })).toBeChecked();
  });

  it("nadie edita su propio alcance: en su fila se explica y no hay selector ni consulta", async () => {
    const { dialog } = await openEdit("Yo Mismo");
    expect(await within(dialog).findByTestId("client-scope-self")).toHaveTextContent(
      /no puedes cambiar tu propio alcance/i,
    );
    expect(
      within(dialog).queryByRole("radio", { name: "Clientes específicos" }),
    ).not.toBeInTheDocument();
    expect(fetchOtUserClientScope).not.toHaveBeenCalled();
  });

  it("el Gestor OT con Usuarios delegado no ve la sección ni al editar ni al invitar", async () => {
    session.isOtAdmin = false;
    const { user, dialog } = await openEdit("Gina Gestora");
    expect(within(dialog).queryByTestId("edit-user-client-scope")).not.toBeInTheDocument();
    expect(fetchOtUserClientScope).not.toHaveBeenCalled();
    await user.click(within(dialog).getByRole("button", { name: "Cancelar" }));

    await user.click(screen.getByRole("button", { name: /invitar/i }));
    const invite = await screen.findByRole("dialog");
    await user.type(within(invite).getByLabelText("Nombre completo"), "Nuevo");
    await user.type(within(invite).getByLabelText("Correo electrónico"), "nuevo@transito.gov.co");
    await user.click(await within(invite).findByRole("radio", { name: "Gestor OT" }));
    expect(within(invite).queryByText("Clientes que gestiona")).not.toBeInTheDocument();
    await user.click(within(invite).getByRole("button", { name: "Enviar invitación" }));
    await waitFor(() => expect(inviteOtUser).toHaveBeenCalled());
    expect(vi.mocked(inviteOtUser).mock.calls[0][0]).not.toHaveProperty("clientScope");
  });

  it("invitar: preselecciona «Todos» y envía clientScope; con específicos envía los ids", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Gina Gestora");
    await user.click(screen.getByRole("button", { name: /invitar/i }));
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByLabelText("Nombre completo"), "Nuevo");
    await user.type(within(dialog).getByLabelText("Correo electrónico"), "nuevo@transito.gov.co");
    await user.click(await within(dialog).findByRole("radio", { name: "Gestor OT" }));
    expect(within(dialog).getByRole("radio", { name: "Todos los clientes" })).toBeChecked();

    await user.click(within(dialog).getByRole("radio", { name: "Clientes específicos" }));
    expect(within(dialog).getByRole("button", { name: "Enviar invitación" })).toBeDisabled();
    await user.click(await within(dialog).findByRole("checkbox", { name: /Carga Gamma/ }));
    await user.click(within(dialog).getByRole("button", { name: "Enviar invitación" }));

    await waitFor(() =>
      expect(inviteOtUser).toHaveBeenCalledWith(
        expect.objectContaining({ clientScope: { mode: "especificos", clientTenantIds: ["c-3"] } }),
        { transitOfficeId: "ot-1" },
      ),
    );
  });
});

describe("OtUsersSection — actor restringido", () => {
  it("invitar: con actorRestricted «Todos» queda deshabilitado, hay nota y el defecto es «específicos»", async () => {
    vi.mocked(fetchOtClientCompanies).mockResolvedValue({
      items: companies.slice(0, 2),
      actorRestricted: true,
    });
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Gina Gestora");
    await user.click(screen.getByRole("button", { name: /invitar/i }));
    const dialog = await screen.findByRole("dialog");
    const todos = await within(dialog).findByRole("radio", { name: "Todos los clientes" });
    expect(todos).toBeDisabled();
    await waitFor(() =>
      expect(within(dialog).getByRole("radio", { name: "Clientes específicos" })).toBeChecked(),
    );
    expect(
      within(dialog).getByText(/solo puedes asignar los clientes que tú gestionas/i),
    ).toBeInTheDocument();
    expect(todos).toHaveAccessibleDescription(/solo puedes asignar/i);
  });

  it("editar: usuario en «Todos» y actor restringido muestra «Todos» marcado pero bloqueado; cambiar a específicos guarda", async () => {
    vi.mocked(fetchOtClientCompanies).mockResolvedValue({
      items: companies,
      actorRestricted: true,
    });
    const { user, dialog } = await openEdit("Gina Gestora");
    const todos = await within(dialog).findByRole("radio", { name: "Todos los clientes" });
    await waitFor(() => expect(todos).toBeDisabled());
    expect(todos).toBeChecked();
    expect(within(dialog).getByTestId("client-scope-restricted-note")).toBeInTheDocument();
    await user.click(within(dialog).getByRole("radio", { name: "Clientes específicos" }));
    await user.click(await within(dialog).findByRole("checkbox", { name: /Carga Gamma/ }));
    await user.click(within(dialog).getByRole("button", { name: "Guardar cambios" }));
    await waitFor(() =>
      expect(setOtUserClientScope).toHaveBeenCalledWith(
        "u-1",
        { mode: "especificos", clientTenantIds: ["c-3"] },
        { transitOfficeId: "ot-1" },
      ),
    );
  });

  it("sin actorRestricted (o false) «Todos» sigue habilitado y sin nota", async () => {
    const { dialog } = await openEdit("Gina Gestora");
    expect(await within(dialog).findByRole("radio", { name: "Todos los clientes" })).toBeEnabled();
    expect(within(dialog).queryByTestId("client-scope-restricted-note")).not.toBeInTheDocument();
  });

  it("CLIENT_SCOPE_EXCEEDS_ACTOR se muestra con un mensaje claro y no guarda el usuario", async () => {
    vi.mocked(setOtUserClientScope).mockRejectedValue(
      new ApiError(403, "Forbidden", { error: "CLIENT_SCOPE_EXCEEDS_ACTOR" }),
    );
    const { user, dialog } = await openEdit("Gina Gestora");
    await user.click(await within(dialog).findByRole("radio", { name: "Clientes específicos" }));
    await user.click(await within(dialog).findByRole("checkbox", { name: /Transportes Alfa/ }));
    await user.click(within(dialog).getByRole("button", { name: "Guardar cambios" }));
    expect(await within(dialog).findByText(/dentro de los que tú gestionas/i)).toBeInTheDocument();
    expect(updateOtUser).not.toHaveBeenCalled();
  });

  it("Gestor OT restringido (su fila trae «específicos») ve la nota al invitar; sin restricción no", async () => {
    session.isOtAdmin = false;
    vi.mocked(fetchOtUsers).mockResolvedValue({
      data: [
        gina,
        {
          ...yo,
          roleCode: "gestor_tramites_ot",
          clientScope: { mode: "especificos", clientCount: 1 },
        },
      ],
    });
    const user = userEvent.setup();
    const { unmount } = renderSection();
    await screen.findByText("Gina Gestora");
    await user.click(screen.getByRole("button", { name: /invitar/i }));
    expect(await screen.findByTestId("invite-inherits-scope-note")).toHaveTextContent(
      /mismos clientes/i,
    );
    unmount();

    vi.mocked(fetchOtUsers).mockResolvedValue({ data: [gina, yo] });
    renderSection();
    await screen.findByText("Gina Gestora");
    await user.click(screen.getByRole("button", { name: /invitar/i }));
    await screen.findByRole("dialog");
    expect(screen.queryByTestId("invite-inherits-scope-note")).not.toBeInTheDocument();
  });
});
