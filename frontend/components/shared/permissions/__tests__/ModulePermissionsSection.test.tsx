// HU #13652 — sección compartida «Permisos por módulo».
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "@/lib/api/types";

vi.mock("@/lib/api/security", () => ({
  getAccessibleModules: vi.fn(),
  getTenantRole: vi.fn(),
}));
vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: { getRole: vi.fn() },
}));
vi.mock("@/lib/api/permission-overrides", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/permission-overrides")>("@/lib/api/permission-overrides");
  return {
    ...actual,
    listGrantablePermissions: vi.fn(),
    listUserOverrides: vi.fn(),
    setUserOverride: vi.fn(),
    removeUserOverride: vi.fn(),
    setModuleOverrides: vi.fn(),
  };
});

import { getAccessibleModules } from "@/lib/api/security";
import { superadminClient } from "@/lib/api/superadmin-client";
import {
  listGrantablePermissions,
  listUserOverrides,
  removeUserOverride,
  setModuleOverrides,
  setUserOverride,
} from "@/lib/api/permission-overrides";
import { ModulePermissionsSection } from "../ModulePermissionsSection";

const modules = [
  {
    id: "m1", code: "tramites", name: "Trámites", sortOrder: 1,
    actions: [
      { id: "p1", slug: "tramites.view", name: "Ver trámites" },
      { id: "p2", slug: "tramites.create", name: "Crear trámites" },
    ],
  },
  { id: "m2", code: "reportes", name: "Reportes", sortOrder: 2, actions: [{ id: "p3", slug: "reportes.view", name: "Ver reportes" }] },
];
const grantable = [
  { id: "p1", slug: "tramites.view", name: "Ver trámites", moduleCode: "tramites", productCode: "tramites" },
  { id: "p2", slug: "tramites.create", name: "Crear trámites", moduleCode: "tramites", productCode: "tramites" },
];
const individual = {
  permissionId: "p2", slug: "tramites.create", productCode: "tramites", effect: "allow" as const,
  source: "INDIVIDUAL" as const, grantedBy: "g", grantedByName: "Ana Admin", grantedAt: "2026-10-19T19:05:00Z",
};

function rejected(status: number, code: string) {
  return new ApiError(status, "x", { code, message: "x" });
}

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(getAccessibleModules).mockResolvedValue(modules);
  vi.mocked(listGrantablePermissions).mockResolvedValue(grantable);
  vi.mocked(listUserOverrides).mockResolvedValue([]);
  vi.mocked(superadminClient.getRole).mockResolvedValue({
    id: "r1", targetEntityType: "COMPANY", code: "x", name: "Rol", description: null, isSystem: false, isActive: true,
    permissions: [{ id: "p1", slug: "tramites.view", name: "Ver trámites" }],
  });
});

const renderSection = (props: Partial<React.ComponentProps<typeof ModulePermissionsSection>> = {}) =>
  render(<ModulePermissionsSection userId="u1" tenantId="t1" mode="superadmin" roleIds={["r1"]} {...props} />);

describe("ModulePermissionsSection", () => {
  it("AC1/AC2/AC6: muestra módulos del catálogo, heredado del rol y solo permisos exigidos", async () => {
    renderSection();
    const tramites = await screen.findByTestId("module-tramites");
    expect(within(tramites).getAllByText("Heredado del rol").length).toBeGreaterThan(0);
    expect(within(tramites).getByRole("switch", { name: /Ver trámites/ })).toHaveAttribute("aria-checked", "true");
    expect(within(tramites).getByRole("switch", { name: /Crear trámites/ })).toHaveAttribute("aria-checked", "false");
    // Reportes no tiene ninguna acción exigida (no es otorgable): no aparece.
    expect(screen.queryByTestId("module-reportes")).not.toBeInTheDocument();
    expect(listGrantablePermissions).toHaveBeenCalledWith("t1");
    expect(listUserOverrides).toHaveBeenCalledWith("u1", "t1");
  });

  it("AC1: un permiso individual marca el módulo Parcial, dice quién y cuándo lo otorgó (DD/MM/YYYY HH:mm)", async () => {
    vi.mocked(listUserOverrides).mockResolvedValue([individual]);
    renderSection();
    const tramites = await screen.findByTestId("module-tramites");
    expect(within(tramites).getByText("Parcial")).toBeInTheDocument();
    expect(within(tramites).getAllByText("Individual").length).toBeGreaterThan(0);
    expect(within(tramites).getByText(/Otorgado por Ana Admin el 19\/10\/2026 14:05/)).toBeInTheDocument();
    expect(within(tramites).getByRole("switch", { name: /Crear trámites/ })).toHaveAttribute("aria-checked", "true");
  });

  it("habilitar una acción heredada deshabilitada envía allow y recarga", async () => {
    vi.mocked(setUserOverride).mockResolvedValue({ permissionId: "p2", effect: "allow", changed: true });
    const onChanged = vi.fn();
    renderSection({ onChanged });
    await userEvent.click(await screen.findByRole("switch", { name: /Crear trámites/ }));
    await waitFor(() => expect(setUserOverride).toHaveBeenCalledWith("u1", "p2", "allow", "t1"));
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
  });

  it("AC3: «Volver al rol» de un permiso individual lo elimina (DELETE)", async () => {
    vi.mocked(listUserOverrides).mockResolvedValue([individual]);
    vi.mocked(removeUserOverride).mockResolvedValue(undefined);
    renderSection();
    await userEvent.click(await screen.findByRole("button", { name: /Volver Crear trámites a lo que dicta el rol/ }));
    await waitFor(() => expect(removeUserOverride).toHaveBeenCalledWith("u1", "p2", "t1"));
  });

  it("módulo completo: «Habilitar módulo» llama al endpoint de módulo y resume lo aplicado", async () => {
    vi.mocked(setModuleOverrides).mockResolvedValue({ productCode: "tramites", batchId: "b", created: 1, skipped: 1, outOfCap: 0, excluded: 0 });
    renderSection();
    await userEvent.click(await screen.findByRole("button", { name: "Habilitar el módulo Trámites completo" }));
    await waitFor(() => expect(setModuleOverrides).toHaveBeenCalledWith("u1", "tramites", "allow", "t1"));
    expect(await screen.findByText(/1 acción aplicada; 1 ya tenían un permiso individual/)).toBeInTheDocument();
  });

  it("muestra el rechazo de la API con mensaje claro según el code", async () => {
    vi.mocked(setUserOverride).mockRejectedValue(rejected(403, "OUT_OF_CAP"));
    renderSection();
    await userEvent.click(await screen.findByRole("switch", { name: /Crear trámites/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Ese permiso queda fuera de lo que puedes otorgar.");
  });

  it("AC4: usuario Super Admin → mensaje y sin controles ni llamadas", () => {
    renderSection({ targetIsSuperAdmin: true });
    expect(screen.getByText("Los Super Admin no tienen permisos individuales.")).toBeInTheDocument();
    expect(screen.queryByRole("switch")).not.toBeInTheDocument();
    expect(listUserOverrides).not.toHaveBeenCalled();
  });

  it("AC4: si la API responde TARGET_SUPERADMIN muestra el mismo mensaje", async () => {
    vi.mocked(listUserOverrides).mockRejectedValue(rejected(403, "TARGET_SUPERADMIN"));
    renderSection();
    expect(await screen.findByText("Los Super Admin no tienen permisos individuales.")).toBeInTheDocument();
  });

  it("AC5: invitación pendiente → sección deshabilitada con el aviso", () => {
    renderSection({ invitationPending: true });
    expect(
      screen.getByText("Los permisos individuales se podrán configurar cuando el usuario acepte la invitación."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("switch")).not.toBeInTheDocument();
    expect(listUserOverrides).not.toHaveBeenCalled();
  });

  it("AC5: si la API responde INVITATION_PENDING muestra el aviso", async () => {
    vi.mocked(listUserOverrides).mockRejectedValue(rejected(409, "INVITATION_PENDING"));
    renderSection();
    expect(await screen.findByText(/cuando el usuario acepte la invitación/)).toBeInTheDocument();
  });

  it("solo lectura: muestra el motivo y deshabilita los controles", async () => {
    vi.mocked(listUserOverrides).mockResolvedValue([individual]);
    renderSection({ readOnly: true, readOnlyReason: "Solo el Super Admin modifica a otro administrador." });
    expect(await screen.findByText("Solo el Super Admin modifica a otro administrador.")).toBeInTheDocument();
    expect(screen.getAllByRole("switch").every((s) => s.hasAttribute("disabled"))).toBe(true);
    expect(screen.queryByRole("button", { name: /Habilitar el módulo/ })).not.toBeInTheDocument();
  });

  it("AC7: estado cargando", () => {
    vi.mocked(listUserOverrides).mockReturnValue(new Promise(() => {}));
    renderSection();
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
  });

  it("AC7: estado error con reintento", async () => {
    vi.mocked(listUserOverrides).mockRejectedValueOnce(new Error("boom"));
    renderSection();
    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /Reintentar/ }));
    expect(await screen.findByTestId("module-tramites")).toBeInTheDocument();
  });

  it("AC7: estado vacío cuando no hay permisos exigidos", async () => {
    vi.mocked(listGrantablePermissions).mockResolvedValue([]);
    renderSection();
    expect(await screen.findByTestId("ui-empty")).toBeInTheDocument();
  });
});
