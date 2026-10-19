// HU #13666 (Feature #13636 F-F) — par Ver / Crear por tipo de servicio en la configuración de permisos por ROL.
// AC1: Crear deshabilitado mientras Ver esté inactivo. AC3: apagar Ver apaga Crear (y la baja viaja en el PUT).
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RbacAdmin } from "../RbacAdmin";
import { SuperadminApiError } from "@/lib/api/superadmin-client";

const getRole = vi.fn();
const setRolePermissions = vi.fn();

vi.mock("@/lib/api/superadmin-client", async () => {
  class SuperadminApiError extends Error {
    constructor(readonly status: number, readonly body: unknown, mensaje: string) {
      super(mensaje);
    }
  }
  return {
    SuperadminApiError,
    superadminClient: {
      listModules: vi.fn().mockResolvedValue([]),
      listCompanies: vi.fn().mockResolvedValue({ data: [] }),
      listRoles: vi.fn((type: string) =>
        Promise.resolve(
          type === "COMPANY"
            ? [{ id: "role-1", targetEntityType: "COMPANY", code: "SUP", name: "Supervisor", description: "", isSystem: false, isActive: true, permissionCount: 2, createdAt: "2026-01-01T00:00:00Z" }]
            : [],
        ),
      ),
      getRole: (...a: unknown[]) => getRole(...a),
      setRolePermissions: (...a: unknown[]) => setRolePermissions(...a),
    },
  };
});

vi.mock("@/lib/api/security", () => ({
  getAccessibleModules: vi.fn().mockResolvedValue([
    { id: "mm", code: "tramites-matriculas", name: "Trámites: Matrículas", sortOrder: 1, actions: [{ id: "mat-r", slug: "matriculas.read", name: "Ver trámites de Matrículas" }, { id: "mat-c", slug: "matriculas.create", name: "Crear trámites de Matrículas" }] },
    { id: "mt", code: "tramites-traspasos", name: "Trámites: Traspasos", sortOrder: 2, actions: [{ id: "tra-r", slug: "traspasos.read", name: "Ver trámites de Traspasos" }, { id: "tra-c", slug: "traspasos.create", name: "Crear trámites de Traspasos" }] },
    { id: "mo", code: "tramites-otros", name: "Trámites: Otros trámites", sortOrder: 3, actions: [{ id: "otr-r", slug: "otros-tramites.read", name: "Ver otros trámites" }, { id: "otr-c", slug: "otros-tramites.create", name: "Crear otros trámites" }] },
  ]),
}));

async function openEditor() {
  const user = userEvent.setup();
  render(<RbacAdmin />);
  await user.click(await screen.findByRole("button", { name: /roles del sistema/i }));
  await user.click(await screen.findByRole("button", { name: /editar permisos de supervisor/i }));
  await screen.findByRole("checkbox", { name: "Ver — Trámites: Matrículas" });
  return user;
}

beforeEach(() => {
  vi.clearAllMocks();
  getRole.mockResolvedValue({ permissions: [{ id: "mat-r" }, { id: "mat-c" }] });
  setRolePermissions.mockResolvedValue({ permissions: [] });
});

describe("RbacAdmin — Ver / Crear por tipo de servicio (HU #13666)", () => {
  it("AC1: muestra el par Ver / Crear de Matrículas, Traspasos y Otros trámites; Crear está deshabilitado si Ver está inactivo", async () => {
    await openEditor();

    for (const name of ["Trámites: Matrículas", "Trámites: Traspasos", "Trámites: Otros trámites"]) {
      expect(screen.getByRole("checkbox", { name: `Ver — ${name}` })).toBeInTheDocument();
      expect(screen.getByRole("checkbox", { name: `Crear — ${name}` })).toBeInTheDocument();
    }
    // Matrículas tiene Ver y Crear activos; Traspasos y Otros no tienen Ver.
    expect(screen.getByRole("checkbox", { name: "Crear — Trámites: Matrículas" })).toBeEnabled();
    expect(screen.getByRole("checkbox", { name: "Crear — Trámites: Traspasos" })).toBeDisabled();
    expect(screen.getByRole("checkbox", { name: "Crear — Trámites: Otros trámites" })).toBeDisabled();

    const user = userEvent.setup();
    await user.click(screen.getByRole("checkbox", { name: "Ver — Trámites: Traspasos" }));
    expect(screen.getByRole("checkbox", { name: "Crear — Trámites: Traspasos" })).toBeEnabled();
  });

  it("AC3: con Ver y Crear activos, al apagar Ver también se apaga Crear y el PUT lleva la baja de ambos", async () => {
    const user = await openEditor();
    await user.click(screen.getByRole("checkbox", { name: "Ver — Trámites: Matrículas" }));

    expect(screen.getByRole("checkbox", { name: "Ver — Trámites: Matrículas" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Crear — Trámites: Matrículas" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Crear — Trámites: Matrículas" })).toBeDisabled();

    // Conserva un permiso para poder guardar (regla de «al menos un permiso»).
    await user.click(screen.getByRole("checkbox", { name: "Ver — Trámites: Traspasos" }));
    await user.click(screen.getByRole("button", { name: /guardar permisos/i }));
    await waitFor(() => expect(setRolePermissions).toHaveBeenCalledWith("role-1", ["tra-r"]));
  });

  it("muestra con mensaje claro el 400 FAMILY_CREATE_REQUIRES_READ", async () => {
    setRolePermissions.mockRejectedValue(new SuperadminApiError(400, { code: "FAMILY_CREATE_REQUIRES_READ" }, "400"));
    const user = await openEditor();
    await user.click(screen.getByRole("button", { name: /guardar permisos/i }));

    const alert = await screen.findByRole("alert");
    expect(within(alert).getByText(/primero debe estar habilitado «Ver»/)).toBeInTheDocument();
  });
});
