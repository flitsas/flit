// HU #10509 — [FRONTEND] Gestión de roles del sistema.
// AC1: crear rol (nombre, tipo de entidad, permisos) → aparece en su tabla.
// AC2: dos tablas separadas (Compañía / Organismo de Tránsito) con editar/eliminar/activar-desactivar.
// AC3: eliminar un rol con usuarios asignados muestra un mensaje claro (no genérico).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RbacAdmin } from "../RbacAdmin";
import { chooseRowAction, findRowAction, rowActionNames } from "@/components/atom/__tests__/rowActionsMenu.helpers";
import { superadminClient } from "@/lib/api/superadmin-client";

const companyRole = {
  id: "role-company-1",
  targetEntityType: "COMPANY",
  code: "SUPERVISOR",
  name: "Supervisor de trámites",
  description: "Rol de compañía",
  isSystem: false,
  isActive: true,
  permissionCount: 1,
  createdAt: "2026-01-01T00:00:00Z",
};

const otRole = {
  id: "role-ot-1",
  targetEntityType: "TRANSIT_OFFICE",
  code: "OT_SUPERVISOR",
  name: "Supervisor OT",
  description: "Rol de organismo",
  isSystem: false,
  isActive: true,
  permissionCount: 0,
  createdAt: "2026-01-01T00:00:00Z",
};

const listRoles = vi.fn();
const createRole = vi.fn();
const deleteRole = vi.fn();
const setRolePermissions = vi.fn();
const activateRole = vi.fn();
const deactivateRole = vi.fn();
const getRole = vi.fn();

vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: {
    listModules: vi.fn().mockResolvedValue([]),
    listCompanies: vi.fn().mockResolvedValue({ data: [] }),
    listRoles: (...args: unknown[]) => listRoles(...args),
    createRole: (...args: unknown[]) => createRole(...args),
    deleteRole: (...args: unknown[]) => deleteRole(...args),
    setRolePermissions: (...args: unknown[]) => setRolePermissions(...args),
    activateRole: (...args: unknown[]) => activateRole(...args),
    deactivateRole: (...args: unknown[]) => deactivateRole(...args),
    getRole: (...args: unknown[]) => getRole(...args),
  },
}));

vi.mock("@/lib/api/security", () => ({
  getAccessibleModules: vi.fn().mockResolvedValue([
    { id: "mod-1", code: "tramites", name: "Trámites", sortOrder: 1, actions: [{ id: "perm-1", slug: "tramites.read", name: "Leer trámites" }] },
  ]),
}));

async function openRolesTab() {
  render(<RbacAdmin />);
  await userEvent.click(await screen.findByRole("button", { name: /roles del sistema/i }));
}

beforeEach(() => {
  vi.clearAllMocks();
  listRoles.mockImplementation((type: string) =>
    Promise.resolve(type === "COMPANY" ? [companyRole] : [otRole]),
  );
});

describe("RbacAdmin — pestaña Roles del sistema (HU #10509)", () => {
  it("AC2: muestra SIEMPRE las dos tablas (Compañía y Organismo de Tránsito) con sus roles", async () => {
    await openRolesTab();

    // HU #13653: cada tabla en su sub-pestaña «Compañía | Organismo de Tránsito»; «Compañía» abre primero.
    const compania = await screen.findByRole("tab", { name: "Compañía" });
    expect(compania).toHaveAttribute("aria-selected", "true");
    expect(await screen.findByText("Supervisor de trámites")).toBeVisible();
    expect(await screen.findByText("Supervisor OT")).not.toBeVisible();

    await userEvent.click(screen.getByRole("tab", { name: "Organismo de Tránsito" }));
    expect(screen.getByText("Supervisor OT")).toBeVisible();
    expect(screen.getByText("Supervisor de trámites")).not.toBeVisible();
  });

  it("AC1: crear un rol lo agrega de inmediato a la tabla de su tipo de entidad", async () => {
    const user = userEvent.setup();
    createRole.mockResolvedValue({ id: "role-new" });
    await openRolesTab();
    await screen.findByRole("tab", { name: "Compañía" });

    // Tras crear, se recarga la tabla COMPANY con el nuevo rol incluido.
    listRoles.mockImplementation((type: string) =>
      Promise.resolve(
        type === "COMPANY" ? [companyRole, { ...companyRole, id: "role-new", name: "Rol Nuevo", code: "NUEVO" }] : [otRole],
      ),
    );

    await user.click(screen.getByRole("button", { name: /nuevo rol/i }));
    await user.type(await screen.findByLabelText(/^nombre/i), "Rol Nuevo");
    // HU #10664 AC2 — hay que marcar al menos un permiso; si no, el formulario se bloquea.
    await user.click(await screen.findByRole("checkbox", { name: /leer trámites/i }));
    await user.click(screen.getByRole("button", { name: /^crear rol$/i }));

    // HU #13653: el payload no lleva `code`; el servidor lo genera a partir del nombre.
    await waitFor(() => expect(createRole).toHaveBeenCalledWith({
      targetEntityType: "COMPANY",
      name: "Rol Nuevo",
      description: undefined,
    }));
    expect(createRole.mock.calls[0][0]).not.toHaveProperty("code");
    await waitFor(() => expect(setRolePermissions).toHaveBeenCalledWith("role-new", ["perm-1"]));
    expect(await screen.findByText("Rol Nuevo")).toBeInTheDocument();
  });

  it("AC2 (#10664): no permite crear un rol sin al menos un permiso", async () => {
    const user = userEvent.setup();
    await openRolesTab();
    await screen.findByRole("tab", { name: "Compañía" });

    await user.click(screen.getByRole("button", { name: /nuevo rol/i }));
    await user.type(await screen.findByLabelText(/^nombre/i), "Sin permisos");
    await user.click(screen.getByRole("button", { name: /^crear rol$/i }));

    expect(await screen.findByText(/al menos un permiso/i)).toBeInTheDocument();
    expect(createRole).not.toHaveBeenCalled();
  });

  it("HU #13653: el editor es una sola lista «Datos del rol» → «Módulos»; sin nombre avisa y lleva el foco al campo", async () => {
    const user = userEvent.setup();
    await openRolesTab();
    await screen.findByRole("tab", { name: "Compañía" });

    await user.click(screen.getByRole("button", { name: /nuevo rol/i }));
    const dialog = await screen.findByRole("dialog");
    await within(dialog).findByRole("checkbox", { name: /leer trámites/i });
    expect(within(dialog).queryByRole("tablist")).not.toBeInTheDocument();
    // Sin tipos de servicio en el catálogo no hay sección «Tipos de servicio».
    expect(within(dialog).getAllByRole("heading", { level: 3 }).map((h) => h.textContent)).toEqual(["Datos del rol", "Módulos"]);

    // HU #13653: «Nuevo rol» ya no pide el código: solo tipo de entidad, nombre y descripción.
    expect(within(dialog).queryByLabelText(/código/i)).not.toBeInTheDocument();
    expect(within(dialog).queryByText(/identificador interno/i)).not.toBeInTheDocument();
    expect(within(dialog).getByLabelText(/tipo de entidad/i)).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^descripción/i)).toBeInTheDocument();
    await user.click(within(dialog).getByRole("checkbox", { name: /leer trámites/i }));
    await user.click(within(dialog).getByRole("button", { name: /^crear rol$/i }));

    expect(await within(dialog).findByText("Escribe un nombre para el rol.")).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^nombre/i)).toHaveFocus();
    expect(createRole).not.toHaveBeenCalled();
  });

  it("HU #13653: cada rol tiene un solo botón «Acciones» con sus opciones; las destructivas al final", async () => {
    const user = userEvent.setup();
    listRoles.mockImplementation((type: string) =>
      Promise.resolve(type === "COMPANY" ? [{ ...companyRole, isSystem: true }] : [otRole]),
    );
    await openRolesTab();
    const companyRow = (await screen.findByText("Supervisor de trámites")).closest("tr") as HTMLElement;

    expect(within(companyRow).queryByRole("button", { name: /editar permisos de/i })).not.toBeInTheDocument();
    expect(await rowActionNames(user, "Supervisor de trámites", within(companyRow))).toEqual([
      "Editar permisos de Supervisor de trámites",
      "Desactivar rol Supervisor de trámites",
      "Eliminar rol Supervisor de trámites",
    ]);
    // Un rol de sistema no se elimina: la opción sigue visible, deshabilitada y con el motivo.
    const eliminar = await findRowAction(user, "Supervisor de trámites", /eliminar rol/i, within(companyRow));
    expect(eliminar).toBeDisabled();
    expect(eliminar).toHaveAttribute("title", "No se puede eliminar: es un rol de sistema");
    expect(eliminar).toHaveTextContent("Eliminar rol");
  });

  it("HU #13653: la tabla de módulos agrupa sus acciones en «Acciones» (agregar permiso, activar/desactivar, eliminar)", async () => {
    const user = userEvent.setup();
    vi.mocked(superadminClient.listModules).mockResolvedValueOnce([
      { id: "m1", code: "reportes", name: "Reportes", description: null, isActive: true, permissionCount: 0, sortOrder: 1 } as never,
    ]);
    render(<RbacAdmin />);
    await screen.findByText("reportes");
    expect(await rowActionNames(user, "Reportes")).toEqual([
      "Agregar permiso a Reportes",
      "Desactivar módulo Reportes",
      "Eliminar módulo Reportes",
    ]);
    expect(screen.queryByRole("button", { name: "+ Permiso" })).not.toBeInTheDocument();
  });

  it("AC3: eliminar un rol con usuarios asignados muestra un mensaje claro (no genérico)", async () => {
    const user = userEvent.setup();
    vi.spyOn(window, "confirm").mockReturnValue(true);
    deleteRole.mockRejectedValue(new Error('409 Conflict: {"code":"ROLE_HAS_ACTIVE_USERS"}'));
    await openRolesTab();

    const companyRow = (await screen.findByText("Supervisor de trámites")).closest("tr") as HTMLElement;
    // HU #13653: las acciones de la fila viven en su menú «Acciones».
    await chooseRowAction(user, "Supervisor de trámites", /eliminar rol/i, within(companyRow));

    expect(await screen.findByText(/tiene usuarios asignados y no puede eliminarse/i)).toBeInTheDocument();
    // No debe mostrarse un mensaje genérico en su lugar.
    expect(screen.queryByText(/no se pudo eliminar el rol/i)).not.toBeInTheDocument();
  });

  it("AC2: desactivar/activar un rol invierte el estado mostrado en la tabla", async () => {
    const user = userEvent.setup();
    deactivateRole.mockResolvedValue(undefined);
    await openRolesTab();

    const companyRow = (await screen.findByText("Supervisor de trámites")).closest("tr") as HTMLElement;
    expect(within(companyRow).getByText("Activo")).toBeInTheDocument();

    await chooseRowAction(user, "Supervisor de trámites", /desactivar rol/i, within(companyRow));

    await waitFor(() => expect(deactivateRole).toHaveBeenCalledWith("role-company-1"));
    expect(within(companyRow).getByText("Inactivo")).toBeInTheDocument();
  });

  it("HU #12964: un permiso de otro producto se avisa y no se manda al guardar (el servidor lo rechazaría)", async () => {
    const user = userEvent.setup();
    getRole.mockResolvedValue({ permissions: [{ id: "perm-1" }, { id: "perm-plataforma" }] });
    setRolePermissions.mockResolvedValue({ permissions: [{ id: "perm-1" }] });
    await openRolesTab();

    const companyRow = (await screen.findByText("Supervisor de trámites")).closest("tr") as HTMLElement;
    await chooseRowAction(user, "Supervisor de trámites", /editar permisos de/i, within(companyRow));

    expect(await screen.findByText(/1 permiso de módulos de otro producto/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /guardar permisos/i }));

    await waitFor(() => expect(setRolePermissions).toHaveBeenCalledWith("role-company-1", ["perm-1"]));
  });
});
