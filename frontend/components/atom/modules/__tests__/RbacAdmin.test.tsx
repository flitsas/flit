// HU #10509 — [FRONTEND] Gestión de roles del sistema.
// AC1: crear rol (nombre, tipo de entidad, permisos) → aparece en su tabla.
// AC2: dos tablas separadas (Compañía / Organismo de Tránsito) con editar/eliminar/activar-desactivar.
// AC3: eliminar un rol con usuarios asignados muestra un mensaje claro (no genérico).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RbacAdmin } from "../RbacAdmin";

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

    expect(await screen.findByText("Roles de Compañía")).toBeInTheDocument();
    expect(await screen.findByText("Roles de Organismo de Tránsito")).toBeInTheDocument();
    expect(await screen.findByText("Supervisor de trámites")).toBeInTheDocument();
    expect(await screen.findByText("Supervisor OT")).toBeInTheDocument();
  });

  it("AC1: crear un rol lo agrega de inmediato a la tabla de su tipo de entidad", async () => {
    const user = userEvent.setup();
    createRole.mockResolvedValue({ id: "role-new" });
    await openRolesTab();
    await screen.findByText("Roles de Compañía");

    // Tras crear, se recarga la tabla COMPANY con el nuevo rol incluido.
    listRoles.mockImplementation((type: string) =>
      Promise.resolve(
        type === "COMPANY" ? [companyRole, { ...companyRole, id: "role-new", name: "Rol Nuevo", code: "NUEVO" }] : [otRole],
      ),
    );

    await user.click(screen.getByRole("button", { name: /nuevo rol/i }));
    await user.type(await screen.findByLabelText(/código/i), "NUEVO");
    await user.type(screen.getByLabelText(/^nombre/i), "Rol Nuevo");
    // HU #10664 AC2 — hay que marcar al menos un permiso; si no, el formulario se bloquea.
    await user.click(await screen.findByRole("checkbox", { name: /leer trámites/i }));
    await user.click(screen.getByRole("button", { name: /^crear rol$/i }));

    await waitFor(() => expect(createRole).toHaveBeenCalledWith({
      targetEntityType: "COMPANY",
      code: "NUEVO",
      name: "Rol Nuevo",
      description: undefined,
    }));
    await waitFor(() => expect(setRolePermissions).toHaveBeenCalledWith("role-new", ["perm-1"]));
    expect(await screen.findByText("Rol Nuevo")).toBeInTheDocument();
  });

  it("AC2 (#10664): no permite crear un rol sin al menos un permiso", async () => {
    const user = userEvent.setup();
    await openRolesTab();
    await screen.findByText("Roles de Compañía");

    await user.click(screen.getByRole("button", { name: /nuevo rol/i }));
    await user.type(await screen.findByLabelText(/código/i), "SINPERM");
    await user.type(screen.getByLabelText(/^nombre/i), "Sin permisos");
    await user.click(screen.getByRole("button", { name: /^crear rol$/i }));

    expect(await screen.findByText(/al menos un permiso/i)).toBeInTheDocument();
    expect(createRole).not.toHaveBeenCalled();
  });

  it("AC3: eliminar un rol con usuarios asignados muestra un mensaje claro (no genérico)", async () => {
    const user = userEvent.setup();
    vi.spyOn(window, "confirm").mockReturnValue(true);
    deleteRole.mockRejectedValue(new Error('409 Conflict: {"code":"ROLE_HAS_ACTIVE_USERS"}'));
    await openRolesTab();

    const companyRow = (await screen.findByText("Supervisor de trámites")).closest("tr") as HTMLElement;
    await user.click(within(companyRow).getByRole("button", { name: /eliminar rol/i }));

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

    await user.click(within(companyRow).getByRole("button", { name: /desactivar rol/i }));

    await waitFor(() => expect(deactivateRole).toHaveBeenCalledWith("role-company-1"));
    expect(within(companyRow).getByText("Inactivo")).toBeInTheDocument();
  });
});
