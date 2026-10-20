// HU #13655/#13656/#13657/#13658 (Feature #13104 F-D2) — el Admin OT configura los permisos de los
// usuarios de su organismo (modo admin), ve a otros administradores en solo lectura, es avisado al
// cambiar un rol y consulta la auditoría de su ámbito.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtUsersSection } from "../OtUsersSection";
import { fetchOtUsers } from "@/lib/api/admin-ot-security";
import type { OtUserItem } from "@/lib/api/admin-ot-security";
import { assignRole, getRoles } from "@/lib/api/security";
import { countUserOverrides, listGrantablePermissions, listPermissionAudit } from "@/lib/api/permission-overrides";
import { ADMIN_READ_ONLY_MESSAGE } from "@/lib/permissions/modulePermissions";

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
  getAccessibleModules: vi.fn().mockResolvedValue([]),
  getTenantRole: vi.fn().mockResolvedValue({ permissions: [] }),
}));
vi.mock("@/lib/api/superadmin-client", () => ({ superadminClient: { getRole: vi.fn(), listRoles: vi.fn() } }));
vi.mock("@/lib/api/permission-overrides", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/permission-overrides")>("@/lib/api/permission-overrides");
  return {
    ...actual,
    listGrantablePermissions: vi.fn().mockResolvedValue([]),
    listUserOverrides: vi.fn().mockResolvedValue([]),
    countUserOverrides: vi.fn(),
    listPermissionAudit: vi.fn(),
  };
});
vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({
    isSuperAdmin: false, isAdminCompany: false, isOtAdmin: true, permissions: [], tenantId: "ot-tenant-1",
    userId: "u-self", roleId: "role-1", roleCode: "ot_admin",
  }),
}));

const mk = (over: Partial<OtUserItem>): OtUserItem => ({
  id: "u-1", fullName: "Ana Torres", email: "ana@transito.gov.co", role: "Operador", roleCode: "operador_ot", roleId: "r-op",
  status: "active", createdAt: "2026-07-01T00:00:00Z", isSuspended: false, rowVersion: 1, ...over,
});
const ana = mk({});
const beto = mk({ id: "u-2", fullName: "Beto Admin", email: "beto@transito.gov.co", role: "Admin OT", roleCode: "ot_admin", roleId: "r-adm" });
const yo = mk({ id: "u-self", fullName: "Yo Mismo", email: "yo@transito.gov.co", role: "Admin OT", roleCode: "ot_admin", roleId: "r-adm" });

const roles = [
  { id: "r-op", code: "operador_ot", name: "Operador", description: null, isSystem: true, permissionCount: 1, createdAt: "", productCode: "tramites" },
  { id: "r-ot2", code: "consulta_ot", name: "Consulta", description: null, isSystem: true, permissionCount: 1, createdAt: "", productCode: "tramites" },
];

const renderSection = () =>
  render(
    <ToastProvider>
      <OtUsersSection transitOfficeId="ot-1" />
    </ToastProvider>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(fetchOtUsers).mockResolvedValue({ data: [ana, beto, yo] });
  vi.mocked(getRoles).mockResolvedValue(roles);
  vi.mocked(listPermissionAudit).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 10 });
});

describe("OtUsersSection — Admin OT (#13655, #13656)", () => {
  it("#13655 AC1: abre «Permisos por módulo» de un usuario del organismo en modo admin", async () => {
    renderSection();
    await screen.findByText("Ana Torres");
    await userEvent.click(screen.getByRole("button", { name: "Permisos por módulo de Ana Torres" }));
    expect(await screen.findByText("Permisos por módulo — Ana Torres")).toBeInTheDocument();
    expect(listGrantablePermissions).toHaveBeenCalledWith(undefined);
    expect(screen.queryByText(ADMIN_READ_ONLY_MESSAGE)).not.toBeInTheDocument();
  });

  it("#13656 AC1: la ficha de otro administrador queda en solo lectura", async () => {
    renderSection();
    await screen.findByText("Beto Admin");
    await userEvent.click(screen.getByRole("button", { name: "Permisos por módulo de Beto Admin" }));
    expect(await screen.findByText(ADMIN_READ_ONLY_MESSAGE)).toBeInTheDocument();
  });

  it("#13656 AC2: la propia ficha queda en solo lectura", async () => {
    renderSection();
    await screen.findByText("Yo Mismo");
    await userEvent.click(screen.getByRole("button", { name: "Permisos por módulo de Yo Mismo" }));
    expect(await screen.findByText(ADMIN_READ_ONLY_MESSAGE)).toBeInTheDocument();
  });
});

describe("OtUsersSection — auditoría del ámbito (#13658)", () => {
  it("AC2: el Admin OT alterna a la auditoría; filtros de usuario y administrador, sin compañía", async () => {
    renderSection();
    await screen.findByText("Ana Torres");
    await userEvent.click(screen.getByRole("button", { name: "Auditoría de permisos" }));
    expect(await screen.findByRole("region", { name: "Auditoría de permisos individuales" })).toBeInTheDocument();
    expect(screen.queryByRole("combobox", { name: "Compañía" })).not.toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Usuario" })).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Administrador" })).toBeInTheDocument();
    expect(listPermissionAudit).toHaveBeenCalledWith(expect.objectContaining({ tenantId: undefined }));
    // La tabla de usuarios se oculta mientras se ve la auditoría y se puede volver.
    expect(screen.queryByText("Ana Torres")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Ver usuarios" }));
    expect(await screen.findByText("Ana Torres")).toBeInTheDocument();
  });

  it("AC3: si la API de auditoría falla muestra el error con reintento", async () => {
    vi.mocked(listPermissionAudit)
      .mockRejectedValueOnce(new Error("boom"))
      .mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 10 });
    renderSection();
    await screen.findByText("Ana Torres");
    await userEvent.click(screen.getByRole("button", { name: "Auditoría de permisos" }));
    expect(await screen.findByText("No se pudo cargar la auditoría de permisos.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(listPermissionAudit).toHaveBeenCalledTimes(2));
    await waitFor(() =>
      expect(screen.queryByText("No se pudo cargar la auditoría de permisos.")).not.toBeInTheDocument(),
    );
  });
});

describe("OtUsersSection — aviso al cambiar el rol (#13657)", () => {
  async function openEditAndPickRole() {
    renderSection();
    await screen.findByText("Ana Torres");
    await userEvent.click(screen.getByRole("button", { name: "Editar usuario Ana Torres" }));
    const dialog = await screen.findByRole("dialog");
    const select = await within(dialog).findByRole("combobox", { name: "Cambiar rol del usuario" });
    await waitFor(() => expect(select).toBeEnabled());
    await userEvent.selectOptions(select, "r-ot2");
    return { dialog, select };
  }

  it("AC1: con 3 permisos individuales avisa antes de confirmar y el rol solo cambia si se confirma", async () => {
    vi.mocked(countUserOverrides).mockResolvedValue({ count: 3 });
    vi.mocked(assignRole).mockResolvedValue(undefined);
    await openEditAndPickRole();
    expect(
      await screen.findByText("Este usuario tiene 3 permisos individuales que se eliminarán al cambiar su rol."),
    ).toBeInTheDocument();
    // Cuenta los permisos del producto del rol nuevo; el Admin OT no manda tenantId.
    expect(countUserOverrides).toHaveBeenCalledWith("u-1", "tramites", undefined, "r-ot2");
    expect(assignRole).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole("button", { name: "Cambiar rol" }));
    await waitFor(() => expect(assignRole).toHaveBeenCalledWith("u-1", "r-ot2"));
  });

  it("AC2: sin permisos individuales no aparece el aviso y el rol cambia directo", async () => {
    vi.mocked(countUserOverrides).mockResolvedValue({ count: 0 });
    vi.mocked(assignRole).mockResolvedValue(undefined);
    await openEditAndPickRole();
    await waitFor(() => expect(assignRole).toHaveBeenCalledWith("u-1", "r-ot2"));
    expect(screen.queryByText(/permisos? individuales? que se eliminar/)).not.toBeInTheDocument();
  });

  it("AC3: cancelar con el aviso abierto no cambia el rol ni toca los permisos", async () => {
    vi.mocked(countUserOverrides).mockResolvedValue({ count: 3 });
    const { select } = await openEditAndPickRole();
    await screen.findByText(/3 permisos individuales/);
    await userEvent.click(screen.getByRole("button", { name: "Mantener rol actual" }));
    expect(screen.queryByText(/3 permisos individuales/)).not.toBeInTheDocument();
    expect(assignRole).not.toHaveBeenCalled();
    expect(select).toHaveValue("r-op");
  });

  it("si no se puede consultar el conteo, avisa de forma genérica y no cambia el rol sin confirmar", async () => {
    vi.mocked(countUserOverrides).mockRejectedValue(new Error("boom"));
    await openEditAndPickRole();
    expect(await screen.findByText(/No pudimos verificar si este usuario tiene permisos individuales/)).toBeInTheDocument();
    expect(assignRole).not.toHaveBeenCalled();
  });
});
