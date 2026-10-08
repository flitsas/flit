// HU #13443 (Feature #13437) — Gestión de roles propios de la compañía para el Admin de Compañía.
// Cubre el listado con sus cuatro estados (AC4), el alta/edición/eliminación con la lista actualizada y los roles
// globales en solo lectura (AC1), y el selector que solo ofrece permisos otorgables con el 403 del tope de
// privilegios traducido a un mensaje claro (AC2). La API (HU #13441) es un doble.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyRolesPanel } from "../CompanyRolesPanel";
import { ApiError } from "@/lib/api/types";
import {
  createTenantRole,
  deleteTenantRole,
  getGrantablePermissions,
  getRoles,
  getTenantRole,
  setTenantRolePermissions,
  updateTenantRole,
  type TenantRole,
  type TenantRoleDetail,
} from "@/lib/api/security";

vi.mock("@/lib/api/security", () => ({
  getRoles: vi.fn(),
  getGrantablePermissions: vi.fn(),
  getTenantRole: vi.fn(),
  createTenantRole: vi.fn(),
  updateTenantRole: vi.fn(),
  setTenantRolePermissions: vi.fn(),
  deleteTenantRole: vi.fn(),
}));

const TENANT = "tenant-1";

const global_: TenantRole = {
  id: "g1",
  code: "Radicador",
  name: "Radicador",
  description: "Rol de FLIT",
  isSystem: true,
  permissionCount: 4,
  createdAt: "2026-01-01T00:00:00Z",
  tenantId: null,
};
const own: TenantRole = {
  id: "o1",
  code: "contador",
  name: "Contador",
  description: null,
  isSystem: false,
  permissionCount: 1,
  createdAt: "2026-02-01T00:00:00Z",
  tenantId: TENANT,
  productCode: "tramites",
};

const permisos = [
  { id: "p1", slug: "tramites.read", name: "Ver trámites", moduleCode: "tramites", productCode: "tramites" },
  { id: "p2", slug: "tramites.write", name: "Crear trámites", moduleCode: "tramites", productCode: "tramites" },
  { id: "p3", slug: "comparendos.read", name: "Ver comparendos", moduleCode: "comparendos", productCode: "comparendos" },
];

const detail: TenantRoleDetail = {
  id: "o1",
  code: "contador",
  name: "Contador",
  description: null,
  isSystem: false,
  isActive: true,
  productCode: "tramites",
  tenantId: TENANT,
  permissions: [{ id: "p1", slug: "tramites.read", name: "Ver trámites" }],
};

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(getRoles).mockResolvedValue([global_, own]);
  vi.mocked(getGrantablePermissions).mockResolvedValue(permisos);
  vi.mocked(getTenantRole).mockResolvedValue(detail);
});

describe("CompanyRolesPanel — estados (AC4)", () => {
  it("cargando: muestra el esqueleto accesible mientras llega el listado", () => {
    vi.mocked(getRoles).mockReturnValue(new Promise(() => {}));
    render(<CompanyRolesPanel />);

    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
  });

  it("lleno: lista los roles con su tipo y permisos", async () => {
    render(<CompanyRolesPanel />);

    expect(await screen.findByText("Contador")).toBeInTheDocument();
    expect(screen.getByText("Propio")).toBeInTheDocument();
    expect(screen.getByText(/FLIT · solo lectura/i)).toBeInTheDocument();
  });

  it("vacío: invita a crear el primer rol", async () => {
    vi.mocked(getRoles).mockResolvedValue([]);
    render(<CompanyRolesPanel />);

    expect(await screen.findByText(/no hay roles todavía/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /nuevo rol/i })).toBeInTheDocument();
  });

  it("error: mensaje claro y reintento que vuelve a pedir el listado", async () => {
    vi.mocked(getRoles).mockRejectedValueOnce(new Error("boom")).mockResolvedValue([own]);
    const ue = userEvent.setup();
    render(<CompanyRolesPanel />);

    expect(await screen.findByText(/no se pudieron cargar los roles/i)).toBeInTheDocument();
    await ue.click(screen.getByRole("button", { name: /reintentar/i }));

    expect(await screen.findByText("Contador")).toBeInTheDocument();
    expect(getRoles).toHaveBeenCalledTimes(2);
  });
});

describe("CompanyRolesPanel — roles globales (AC1)", () => {
  it("el rol global solo se puede ver: sin editar ni eliminar, y el modal es de solo lectura", async () => {
    vi.mocked(getTenantRole).mockResolvedValue({ ...detail, id: "g1", code: "Radicador", name: "Radicador", tenantId: null });
    const ue = userEvent.setup();
    render(<CompanyRolesPanel />);
    await screen.findByText("Contador");

    expect(screen.queryByRole("button", { name: /editar radicador/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /eliminar radicador/i })).not.toBeInTheDocument();
    await ue.click(screen.getByRole("button", { name: /ver permisos de radicador/i }));

    const dialog = await screen.findByRole("dialog");
    expect(await within(dialog).findByText("tramites.read")).toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: /guardar/i })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole("checkbox")).not.toBeInTheDocument();
    expect(getGrantablePermissions).not.toHaveBeenCalled();
  });
});

describe("CompanyRolesPanel — crear, editar y eliminar (AC1)", () => {
  it("crea un rol con los permisos elegidos y recarga el listado", async () => {
    vi.mocked(createTenantRole).mockResolvedValue({ id: "o2" });
    const ue = userEvent.setup();
    render(<CompanyRolesPanel />);
    await screen.findByText("Contador");

    await ue.click(screen.getByRole("button", { name: /nuevo rol/i }));
    const dialog = await screen.findByRole("dialog");
    await ue.type(await within(dialog).findByLabelText(/^código/i), "auditor_ext");
    await ue.type(within(dialog).getByLabelText(/^nombre/i), "Auditor externo");
    await ue.click(within(dialog).getByRole("checkbox", { name: /ver trámites/i }));
    await ue.click(within(dialog).getByRole("button", { name: /crear rol/i }));

    await waitFor(() =>
      expect(createTenantRole).toHaveBeenCalledWith({
        code: "auditor_ext",
        name: "Auditor externo",
        description: null,
        productCode: "tramites",
        permissionIds: ["p1"],
      }),
    );
    await waitFor(() => expect(getRoles).toHaveBeenCalledTimes(2));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("AC2: el selector ofrece solo los permisos otorgables del producto del rol", async () => {
    const ue = userEvent.setup();
    render(<CompanyRolesPanel />);
    await screen.findByText("Contador");

    await ue.click(screen.getByRole("button", { name: /nuevo rol/i }));
    const dialog = await screen.findByRole("dialog");

    expect(await within(dialog).findByRole("checkbox", { name: /ver trámites/i })).toBeInTheDocument();
    expect(within(dialog).getByRole("checkbox", { name: /crear trámites/i })).toBeInTheDocument();
    // Comparendos es otro producto: un rol no mezcla productos.
    expect(within(dialog).queryByRole("checkbox", { name: /ver comparendos/i })).not.toBeInTheDocument();
    expect(within(dialog).getByRole("combobox", { name: /producto/i })).toBeInTheDocument();
  });

  it("AC2: un 403 de tope de privilegios se muestra con un mensaje claro y conserva lo escrito", async () => {
    vi.mocked(createTenantRole).mockRejectedValue(
      new ApiError(403, "forbidden", { code: "PERMISSION_NOT_HELD", permissions: ["tramites.write"] }),
    );
    const ue = userEvent.setup();
    render(<CompanyRolesPanel />);
    await screen.findByText("Contador");

    await ue.click(screen.getByRole("button", { name: /nuevo rol/i }));
    const dialog = await screen.findByRole("dialog");
    await ue.type(await within(dialog).findByLabelText(/^código/i), "auditor_ext");
    await ue.type(within(dialog).getByLabelText(/^nombre/i), "Auditor externo");
    await ue.click(within(dialog).getByRole("button", { name: /crear rol/i }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(/solo puedes otorgar permisos que tú mismo tienes/i);
    expect(within(dialog).getByLabelText(/^nombre/i)).toHaveValue("Auditor externo");
  });

  it("valida el código antes de llamar a la API", async () => {
    const ue = userEvent.setup();
    render(<CompanyRolesPanel />);
    await screen.findByText("Contador");

    await ue.click(screen.getByRole("button", { name: /nuevo rol/i }));
    const dialog = await screen.findByRole("dialog");
    await ue.type(await within(dialog).findByLabelText(/^código/i), "con espacio");
    await ue.type(within(dialog).getByLabelText(/^nombre/i), "X");
    await ue.click(within(dialog).getByRole("button", { name: /crear rol/i }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(/de 2 a 50 caracteres/i);
    expect(createTenantRole).not.toHaveBeenCalled();
  });

  it("edita nombre y permisos de un rol propio", async () => {
    vi.mocked(updateTenantRole).mockResolvedValue(detail);
    vi.mocked(setTenantRolePermissions).mockResolvedValue(detail);
    const ue = userEvent.setup();
    render(<CompanyRolesPanel />);
    await screen.findByText("Contador");

    await ue.click(screen.getByRole("button", { name: /editar contador/i }));
    const dialog = await screen.findByRole("dialog");
    const nombre = await within(dialog).findByLabelText(/^nombre/i);
    expect(within(dialog).getByLabelText(/^código/i)).toBeDisabled();
    expect(within(dialog).getByRole("checkbox", { name: /ver trámites/i })).toBeChecked();
    await ue.clear(nombre);
    await ue.type(nombre, "Contador senior");
    await ue.click(within(dialog).getByRole("checkbox", { name: /crear trámites/i }));
    await ue.click(within(dialog).getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() => expect(updateTenantRole).toHaveBeenCalledWith("o1", { name: "Contador senior", description: null }));
    expect(setTenantRolePermissions).toHaveBeenCalledWith("o1", expect.arrayContaining(["p1", "p2"]));
  });

  it("elimina un rol propio y recarga la lista", async () => {
    vi.mocked(deleteTenantRole).mockResolvedValue(undefined);
    const ue = userEvent.setup();
    render(<CompanyRolesPanel />);
    await screen.findByText("Contador");

    await ue.click(screen.getByRole("button", { name: /eliminar contador/i }));
    const dialog = await screen.findByRole("dialog");
    await ue.click(within(dialog).getByRole("button", { name: /^eliminar$/i }));

    await waitFor(() => expect(deleteTenantRole).toHaveBeenCalledWith("o1"));
    await waitFor(() => expect(getRoles).toHaveBeenCalledTimes(2));
  });

  it("eliminar un rol asignado a usuarios (409) explica qué hacer", async () => {
    vi.mocked(deleteTenantRole).mockRejectedValue(new ApiError(409, "conflict", { code: "ROLE_HAS_ACTIVE_USERS" }));
    const ue = userEvent.setup();
    render(<CompanyRolesPanel />);
    await screen.findByText("Contador");

    await ue.click(screen.getByRole("button", { name: /eliminar contador/i }));
    const dialog = await screen.findByRole("dialog");
    await ue.click(within(dialog).getByRole("button", { name: /^eliminar$/i }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(/asignado a usuarios/i);
    expect(getRoles).toHaveBeenCalledTimes(1);
  });
});
