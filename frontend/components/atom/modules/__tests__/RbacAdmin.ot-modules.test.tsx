// HU #13644 — los permisos OT solo aplican en un organismo de tránsito: el editor de un rol de compañía no ofrece
// los módulos OT (misma decisión que en los permisos por usuario) y el editor de un rol OT sí.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RbacAdmin } from "../RbacAdmin";

const getRole = vi.fn();
const setRolePermissions = vi.fn();

const role = (id: string, targetEntityType: string, name: string) => ({
  id, targetEntityType, code: id, name, description: "", isSystem: false, isActive: true, permissionCount: 1, createdAt: "2026-01-01T00:00:00Z",
});

vi.mock("@/lib/api/superadmin-client", async () => {
  class SuperadminApiError extends Error {}
  return {
    SuperadminApiError,
    superadminClient: {
      listModules: vi.fn().mockResolvedValue([]),
      listCompanies: vi.fn().mockResolvedValue({ data: [] }),
      listRoles: vi.fn((type: string) =>
        Promise.resolve(type === "COMPANY" ? [role("rad", "COMPANY", "Radicador")] : [role("ota", "TRANSIT_OFFICE", "Administrador OT")]),
      ),
      getRole: (...a: unknown[]) => getRole(...a),
      setRolePermissions: (...a: unknown[]) => setRolePermissions(...a),
    },
  };
});

vi.mock("@/lib/api/security", () => ({
  getAccessibleModules: vi.fn().mockResolvedValue([
    { id: "mt", code: "tramites", name: "Trámites", sortOrder: 1, actions: [{ id: "tr-v", slug: "tramites.view", name: "Ver trámites" }] },
    { id: "mot", code: "ot-mandatos", name: "OT: Mandatos", sortOrder: 2, actions: [{ id: "ot-m", slug: "ot.mandatos.manage", name: "Gestionar mandatos" }] },
  ]),
}));

async function openEditor(roleName: string, entity: "Compañía" | "Organismo de Tránsito" = "Compañía") {
  const user = userEvent.setup();
  render(<RbacAdmin />);
  for (const b of await screen.findAllByRole("button", { name: /roles del sistema/i })) await user.click(b);
  // HU #13653: las tablas de roles viven en sub-pestañas por tipo de entidad.
  await user.click(await screen.findByRole("tab", { name: entity }));
  await user.click(await screen.findByRole("button", { name: `Editar permisos de ${roleName}` }));
  await screen.findByText("Ver trámites");
  return user;
}

beforeEach(() => {
  vi.clearAllMocks();
  getRole.mockResolvedValue({ permissions: [{ id: "tr-v" }] });
  setRolePermissions.mockResolvedValue({ permissions: [] });
});

describe("RbacAdmin — módulos OT según el tipo de rol (HU #13644)", () => {
  it("un rol de compañía no ofrece los módulos OT", async () => {
    await openEditor("Radicador");
    expect(screen.queryByText("OT: Mandatos")).not.toBeInTheDocument();
    expect(screen.queryByText("Gestionar mandatos")).not.toBeInTheDocument();
  });

  it("un rol OT sí ofrece los módulos OT", async () => {
    await openEditor("Administrador OT", "Organismo de Tránsito");
    expect(screen.getByText("OT: Mandatos")).toBeInTheDocument();
  });

  it("un rol de compañía que ya tenía un permiso OT avisa y lo quita al guardar", async () => {
    getRole.mockResolvedValue({ permissions: [{ id: "tr-v" }, { id: "ot-m" }] });
    const user = await openEditor("Radicador");
    expect(await screen.findByText(/1 permiso del organismo de tránsito, que no\s+aplica a un rol de compañía/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /guardar/i }));
    expect(setRolePermissions).toHaveBeenCalledWith("rad", ["tr-v"]);
  });
});
