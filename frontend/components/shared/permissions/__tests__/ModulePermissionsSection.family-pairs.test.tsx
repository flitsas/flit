// HU #13666 (Feature #13636 F-F) — par Ver / Crear por tipo de servicio en «Permisos por módulo» de un usuario.
// AC2: mismo par con «Heredado del rol» e «Individual». AC3: apagar Ver apaga Crear; Crear deshabilitado sin Ver efectivo.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "@/lib/api/types";

vi.mock("@/lib/api/security", () => ({ getAccessibleModules: vi.fn(), getTenantRole: vi.fn() }));
vi.mock("@/lib/api/superadmin-client", () => ({ superadminClient: { getRole: vi.fn() } }));
vi.mock("@/lib/api/permission-overrides", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/permission-overrides")>("@/lib/api/permission-overrides");
  return {
    ...actual,
    listGrantablePermissions: vi.fn(),
    listUserOverrides: vi.fn(),
    listUserRolePermissionIds: vi.fn(),
    setUserOverride: vi.fn(),
    removeUserOverride: vi.fn(),
    setModuleOverrides: vi.fn(),
  };
});

import { getAccessibleModules } from "@/lib/api/security";
import {
  listGrantablePermissions,
  listUserOverrides,
  listUserRolePermissionIds,
  removeUserOverride,
  setUserOverride,
} from "@/lib/api/permission-overrides";
import { ModulePermissionsSection } from "../ModulePermissionsSection";

const FAMILIES = [
  { code: "tramites-matriculas", name: "Trámites: Matrículas", r: "mat-r", c: "mat-c", slug: "matriculas" },
  { code: "tramites-traspasos", name: "Trámites: Traspasos", r: "tra-r", c: "tra-c", slug: "traspasos" },
  { code: "tramites-otros", name: "Trámites: Otros trámites", r: "otr-r", c: "otr-c", slug: "otros-tramites" },
];
const modules = FAMILIES.map((f, i) => ({
  id: `m${i}`, code: f.code, name: f.name, sortOrder: i,
  actions: [
    { id: f.r, slug: `${f.slug}.read`, name: `Ver ${f.name}` },
    { id: f.c, slug: `${f.slug}.create`, name: `Crear ${f.name}` },
  ],
}));
const grantable = FAMILIES.flatMap((f) => [
  { id: f.r, slug: `${f.slug}.read`, name: "Ver", moduleCode: f.code, productCode: "tramites" },
  { id: f.c, slug: `${f.slug}.create`, name: "Crear", moduleCode: f.code, productCode: "tramites" },
]);
const override = (permissionId: string, slug: string, effect: "allow" | "deny") => ({
  permissionId, slug, productCode: "tramites", effect, source: "INDIVIDUAL" as const, grantedBy: "g", grantedByName: "Ana", grantedAt: "2026-10-19T19:05:00Z",
});

const MAT = "Trámites: Matrículas";
const sw = (label: "Ver" | "Crear", name = MAT) => screen.findByRole("switch", { name: `${label}, ${name}` });

// Lo que da el rol: la API lo calcula con todos los roles del usuario (GET .../role-permissions).
function setRole(ids: string[]) {
  vi.mocked(listUserRolePermissionIds).mockResolvedValue({ permissionIds: ids });
}

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(getAccessibleModules).mockResolvedValue(modules);
  vi.mocked(listGrantablePermissions).mockResolvedValue(grantable);
  vi.mocked(listUserOverrides).mockResolvedValue([]);
  setRole(["mat-r", "mat-c"]);
});

// Solo hay tipos de servicio en este catálogo: la ficha los lista sin pestañas (HU #13653).
async function renderSection() {
  render(<ModulePermissionsSection userId="u1" tenantId="t1" mode="superadmin" roleIds={["r1"]} />);
  await screen.findByTestId("module-tramites-matriculas");
  expect(screen.queryByRole("tablist")).not.toBeInTheDocument();
}

describe("ModulePermissionsSection — sub-pestañas «Módulos | Tipos de servicio» (HU #13653)", () => {
  const inicio = { id: "m-ini", code: "dashboard", name: "Inicio", sortOrder: -1, actions: [{ id: "ini-r", slug: "dashboard.read", name: "Ver inicio" }] };

  it("separa los módulos de los tipos de servicio; «Módulos» abre primero", async () => {
    vi.mocked(getAccessibleModules).mockResolvedValue([inicio, ...modules]);
    vi.mocked(listGrantablePermissions).mockResolvedValue([
      { id: "ini-r", slug: "dashboard.read", name: "Ver inicio", moduleCode: "dashboard", productCode: "tramites" },
      ...grantable,
    ]);
    render(<ModulePermissionsSection userId="u1" tenantId="t1" mode="superadmin" roleIds={["r1"]} />);

    const modulos = await screen.findByRole("tab", { name: "Módulos" });
    expect(modulos).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("switch", { name: "Ver inicio, módulo Inicio" })).toBeVisible();
    expect(screen.getByTestId("module-tramites-matriculas")).not.toBeVisible();

    await userEvent.click(screen.getByRole("tab", { name: "Tipos de servicio" }));
    expect(screen.getByTestId("module-tramites-matriculas")).toBeVisible();
    expect(screen.getByTestId("module-dashboard")).not.toBeVisible();

    // Teclado: flecha izquierda vuelve a «Módulos».
    await userEvent.keyboard("{ArrowLeft}");
    expect(screen.getByRole("tab", { name: "Módulos" })).toHaveAttribute("aria-selected", "true");
  });

  it("sin tipos de servicio (p. ej. la ficha del organismo) no hay pestañas, solo la lista", async () => {
    vi.mocked(getAccessibleModules).mockResolvedValue([inicio, ...modules]);
    vi.mocked(listGrantablePermissions).mockResolvedValue([
      { id: "ini-r", slug: "dashboard.read", name: "Ver inicio", moduleCode: "dashboard", productCode: "tramites" },
      ...grantable,
    ]);
    render(
      <ModulePermissionsSection userId="u1" mode="admin" moduleFilter={(code) => code === "dashboard"} />,
    );

    expect(await screen.findByRole("switch", { name: "Ver inicio, módulo Inicio" })).toBeVisible();
    expect(screen.queryByRole("tablist")).not.toBeInTheDocument();
  });
});

describe("ModulePermissionsSection — Ver / Crear por tipo de servicio (HU #13666)", () => {
  it("AC2: cada tipo de servicio tiene el par Ver / Crear con «Heredado del rol»", async () => {
    await renderSection();
    for (const f of FAMILIES) {
      const li = await screen.findByTestId(`module-${f.code}`);
      expect(within(li).getByRole("switch", { name: `Ver, ${f.name}` })).toBeInTheDocument();
      expect(within(li).getByRole("switch", { name: `Crear, ${f.name}` })).toBeInTheDocument();
    }
    const mat = screen.getByTestId("module-tramites-matriculas");
    expect(within(mat).getAllByText("Heredado del rol")).toHaveLength(3); // módulo + Ver + Crear
    expect(await sw("Ver")).toHaveAttribute("aria-checked", "true");
    expect(await sw("Crear")).toHaveAttribute("aria-checked", "true");
  });

  it("AC2: un permiso individual muestra el estado «Individual» en su fila", async () => {
    setRole([]);
    vi.mocked(listUserOverrides).mockResolvedValue([override("mat-r", "matriculas.read", "allow")]);
    await renderSection();
    const mat = await screen.findByTestId("module-tramites-matriculas");
    expect(within(within(mat).getByTestId("family-read-matriculas.read")).getByText("Individual")).toBeInTheDocument();
    expect(within(within(mat).getByTestId("family-create-matriculas.create")).getByText("Heredado del rol")).toBeInTheDocument();
  });

  it("Crear está deshabilitado mientras el Ver efectivo (heredado) esté inactivo", async () => {
    setRole([]);
    await renderSection();
    expect(await sw("Crear")).toBeDisabled();
    expect(await sw("Ver")).toBeEnabled();
  });

  it("Crear está deshabilitado si el Ver heredado está denegado individualmente", async () => {
    vi.mocked(listUserOverrides).mockResolvedValue([override("mat-r", "matriculas.read", "deny")]);
    await renderSection();
    expect(await sw("Ver")).toHaveAttribute("aria-checked", "false");
    expect(await sw("Crear")).toBeDisabled();
  });

  it("al habilitar Ver, Crear queda disponible", async () => {
    setRole([]);
    vi.mocked(setUserOverride).mockResolvedValue({ permissionId: "mat-r", effect: "allow", changed: true });
    await renderSection();
    await userEvent.click(await sw("Ver"));
    await waitFor(() => expect(setUserOverride).toHaveBeenCalledWith("u1", "mat-r", "allow", "t1"));
    vi.mocked(listUserOverrides).mockResolvedValue([override("mat-r", "matriculas.read", "allow")]);
  });

  it("AC3: con Ver y Crear activos, al apagar Ver (deny) la API arrastra Crear y la UI lo refleja apagado y deshabilitado", async () => {
    vi.mocked(setUserOverride).mockImplementation(async () => {
      // La API retira Crear junto con Ver (cascada): el listado ya devuelve ambos denegados.
      vi.mocked(listUserOverrides).mockResolvedValue([override("mat-r", "matriculas.read", "deny"), override("mat-c", "matriculas.create", "deny")]);
      return { permissionId: "mat-r", effect: "deny", changed: true };
    });
    await renderSection();
    expect(await sw("Crear")).toHaveAttribute("aria-checked", "true");

    await userEvent.click(await sw("Ver"));

    await waitFor(() => expect(setUserOverride).toHaveBeenCalledWith("u1", "mat-r", "deny", "t1"));
    await waitFor(async () => expect(await sw("Crear")).toHaveAttribute("aria-checked", "false"));
    expect(await sw("Crear")).toBeDisabled();
    expect(await sw("Ver")).toHaveAttribute("aria-checked", "false");
  });

  it("AC3: «Volver al rol» del allow de Ver apaga Crear cuando el rol no da Ver", async () => {
    setRole([]);
    vi.mocked(listUserOverrides).mockResolvedValue([override("mat-r", "matriculas.read", "allow"), override("mat-c", "matriculas.create", "allow")]);
    vi.mocked(removeUserOverride).mockImplementation(async () => {
      vi.mocked(listUserOverrides).mockResolvedValue([]);
    });
    await renderSection();
    expect(await sw("Crear")).toBeEnabled();

    await userEvent.click(await screen.findByRole("button", { name: `Volver Ver (${MAT}) a lo que dicta el rol` }));

    await waitFor(() => expect(removeUserOverride).toHaveBeenCalledWith("u1", "mat-r", "t1"));
    await waitFor(async () => expect(await sw("Crear")).toBeDisabled());
    expect(await sw("Crear")).toHaveAttribute("aria-checked", "false");
  });

  it("muestra el 400 FAMILY_CREATE_REQUIRES_READ con un mensaje claro", async () => {
    setRole(["mat-r"]);
    vi.mocked(setUserOverride).mockRejectedValue(new ApiError(400, "x", { code: "FAMILY_CREATE_REQUIRES_READ", message: "x" }));
    await renderSection();
    await userEvent.click(await sw("Crear"));
    expect(await screen.findByText(/primero debe estar habilitado «Ver»/)).toBeInTheDocument();
  });
});
