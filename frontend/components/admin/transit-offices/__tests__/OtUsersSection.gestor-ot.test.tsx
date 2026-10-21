// HU #13655 (Feature #13104 F-D2) — rol «Gestor OT»: el Admin OT lo invita y le ajusta los permisos de los módulos del
// organismo (ficha editable, sin módulos de compañía ni Ver/Crear por familia). Un Gestor OT con Usuarios delegado
// invita solo roles no administradores, no cambia roles ni edita administradores.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import { fetchOtUsers, inviteOtUser } from "@/lib/api/admin-ot-security";
import type { OtUserItem } from "@/lib/api/admin-ot-security";
import { getAccessibleModules, getRoles } from "@/lib/api/security";
import { listGrantablePermissions } from "@/lib/api/permission-overrides";
import { ADMIN_READ_ONLY_MESSAGE } from "@/lib/permissions/modulePermissions";
import { chooseRowAction, rowActionNames } from "@/components/atom/__tests__/rowActionsMenu.helpers";

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
}));
vi.mock("@/lib/api/security", () => ({
  restoreUser: vi.fn(),
  assignRole: vi.fn(),
  getRoles: vi.fn(),
  getAccessibleModules: vi.fn(),
  getTenantRole: vi.fn().mockResolvedValue({ permissions: [] }),
}));
vi.mock("@/lib/api/superadmin-client", () => ({ superadminClient: { getRole: vi.fn(), listRoles: vi.fn() } }));
vi.mock("@/lib/api/permission-overrides", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/permission-overrides")>("@/lib/api/permission-overrides");
  return {
    ...actual,
    listGrantablePermissions: vi.fn(),
    listUserOverrides: vi.fn().mockResolvedValue([]),
    listUserRolePermissionIds: vi.fn().mockResolvedValue({ permissionIds: ["p-tramites"] }),
    countUserOverrides: vi.fn().mockResolvedValue({ count: 0 }),
    listPermissionAudit: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 10 }),
  };
});

const session = vi.hoisted(() => ({ isOtAdmin: true }));
vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: false,
    isAdminCompany: false,
    isOtAdmin: session.isOtAdmin,
    permissions: [],
    tenantId: "ot-tenant-1",
    userId: "u-self",
    roleId: session.isOtAdmin ? "r-adm" : "r-gestor",
    roleCode: session.isOtAdmin ? "ot_admin" : "gestor_tramites_ot",
  }),
}));

const mk = (over: Partial<OtUserItem>): OtUserItem => ({
  id: "u-1", fullName: "Gina Gestora", email: "gina@transito.gov.co", role: "Gestor OT", roleCode: "gestor_tramites_ot",
  roleId: "r-gestor", status: "active", createdAt: "2026-10-01T00:00:00Z", isSuspended: false, rowVersion: 1, ...over,
});
const gina = mk({});
const beto = mk({ id: "u-2", fullName: "Beto Admin", email: "beto@transito.gov.co", role: "Administrador OT", roleCode: "ot_admin", roleId: "r-adm" });

const roles = [
  { id: "r-adm", code: "ot_admin", name: "Administrador OT", description: null, isSystem: true, permissionCount: 8, createdAt: "", productCode: "tramites" },
  { id: "r-gestor", code: "gestor_tramites_ot", name: "Gestor OT", description: null, isSystem: true, permissionCount: 2, createdAt: "", productCode: "tramites" },
];

// Catálogo del Admin OT: módulos del organismo, la descarga masiva y, por su tope, módulos de compañía y de familia.
const modules = [
  { id: "m1", code: "ot-tramites", name: "OT: Trámites", sortOrder: 1, productCode: "tramites", actions: [{ id: "p-tramites", slug: "ot.tramites.manage", name: "Gestionar trámites del organismo" }] },
  { id: "m2", code: "ot-documentos", name: "OT: Documentos", sortOrder: 2, productCode: "tramites", actions: [{ id: "p-docs", slug: "ot.documentos.manage", name: "Gestionar prelación documental" }] },
  { id: "m3", code: "consolidado-masivo", name: "Descarga masiva de consolidados", sortOrder: 3, productCode: "tramites", actions: [{ id: "p-masivo", slug: "consolidado-masivo.download", name: "Descargar consolidados en lote" }] },
  { id: "m4", code: "matriculas", name: "Matrículas", sortOrder: 4, productCode: "tramites", actions: [{ id: "p-mat", slug: "matriculas.read", name: "Ver matrículas" }] },
  { id: "m5", code: "tramites", name: "Trámites de compañía", sortOrder: 5, productCode: "tramites", actions: [{ id: "p-tr", slug: "tramites.read", name: "Ver trámites" }] },
];
const grantable = modules.map((m) => ({ id: m.actions[0].id, slug: m.actions[0].slug, name: m.actions[0].name, moduleCode: m.code, productCode: "tramites" }));

const renderSection = () =>
  render(
    <ToastProvider>
      <OtUsersSection transitOfficeId="ot-1" />
    </ToastProvider>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  session.isOtAdmin = true;
  vi.mocked(fetchOtUsers).mockResolvedValue({ data: [gina, beto] });
  vi.mocked(getRoles).mockResolvedValue(roles);
  vi.mocked(getAccessibleModules).mockResolvedValue(modules);
  vi.mocked(listGrantablePermissions).mockResolvedValue(grantable);
  vi.mocked(inviteOtUser).mockResolvedValue({ invitationId: "i-1", email: "x", emailSent: true } as never);
});

describe("OtUsersSection — Admin OT y el rol Gestor OT (#13655)", () => {
  it("invita a un usuario con el rol Gestor OT", async () => {
    renderSection();
    await screen.findByText("Gina Gestora");
    await userEvent.click(screen.getByRole("button", { name: /invitar/i }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByLabelText("Nombre completo"), "Nuevo Gestor");
    await userEvent.type(within(dialog).getByLabelText("Correo electrónico"), "nuevo@transito.gov.co");
    expect(await within(dialog).findByRole("radio", { name: "Administrador OT" })).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole("radio", { name: "Gestor OT" }));
    await userEvent.click(within(dialog).getByRole("button", { name: "Enviar invitación" }));

    await waitFor(() =>
      expect(inviteOtUser).toHaveBeenCalledWith(
        { email: "nuevo@transito.gov.co", fullName: "Nuevo Gestor", roleIds: ["r-gestor"] },
        expect.anything(),
      ),
    );
  });

  it("la ficha de un Gestor OT es editable y solo muestra los módulos del organismo", async () => {
    renderSection();
    await screen.findByText("Gina Gestora");
    await chooseRowAction(userEvent, "Gina Gestora", "Permisos por módulo de Gina Gestora");

    const toggle = await screen.findByRole("switch", { name: "Gestionar prelación documental, módulo OT: Documentos" });
    expect(toggle).toBeEnabled();
    expect(screen.queryByText(ADMIN_READ_ONLY_MESSAGE)).not.toBeInTheDocument();
    expect(screen.getByRole("switch", { name: "Gestionar trámites del organismo, módulo OT: Trámites" })).toBeInTheDocument();
    expect(screen.getByRole("switch", { name: /módulo Descarga masiva de consolidados/ })).toBeInTheDocument();
    expect(screen.queryByRole("switch", { name: /módulo Matrículas/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("switch", { name: /módulo Trámites de compañía/ })).not.toBeInTheDocument();
  });

  it("puede cambiar el rol del Gestor OT (la ficha de edición ofrece el selector de rol)", async () => {
    renderSection();
    await screen.findByText("Gina Gestora");
    await chooseRowAction(userEvent, "Gina Gestora", "Editar usuario Gina Gestora");
    const dialog = await screen.findByRole("dialog");
    const select = await within(dialog).findByRole("combobox", { name: "Cambiar rol del usuario" });
    await waitFor(() => expect(select).toBeEnabled());
    expect(within(select).getByRole("option", { name: "Administrador OT" })).toBeInTheDocument();
  });
});

describe("OtUsersSection — Gestor OT con Usuarios delegado (#13655)", () => {
  beforeEach(() => {
    session.isOtAdmin = false;
  });

  it("no ofrece el rol de administrador al invitar y exige elegir un rol", async () => {
    renderSection();
    await screen.findByText("Gina Gestora");
    await userEvent.click(screen.getByRole("button", { name: /invitar/i }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByLabelText("Nombre completo"), "Otro Gestor");
    await userEvent.type(within(dialog).getByLabelText("Correo electrónico"), "otro@transito.gov.co");

    expect(await within(dialog).findByRole("radio", { name: "Gestor OT" })).toBeInTheDocument();
    expect(within(dialog).queryByRole("radio", { name: "Administrador OT" })).not.toBeInTheDocument();
    const enviar = within(dialog).getByRole("button", { name: "Enviar invitación" });
    expect(enviar).toBeDisabled();
    await userEvent.click(within(dialog).getByRole("radio", { name: "Gestor OT" }));
    expect(enviar).toBeEnabled();
  });

  it("no edita a un administrador, no cambia roles y no configura permisos", async () => {
    renderSection();
    await screen.findByText("Beto Admin");
    // HU #13653: las acciones viven en el menú «Acciones» de cada fila.
    const beto = await rowActionNames(userEvent, "Beto Admin");
    expect(beto).not.toContain("Editar usuario Beto Admin");
    expect(beto).not.toContainEqual(expect.stringMatching(/Permisos por módulo de/));
    expect(await rowActionNames(userEvent, "Gina Gestora")).not.toContainEqual(expect.stringMatching(/Permisos por módulo de/));

    await chooseRowAction(userEvent, "Gina Gestora", "Editar usuario Gina Gestora");
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByLabelText(/nombre completo/i)).toHaveValue("Gina Gestora");
    expect(within(dialog).queryByRole("combobox", { name: "Cambiar rol del usuario" })).not.toBeInTheDocument();
  });
});
