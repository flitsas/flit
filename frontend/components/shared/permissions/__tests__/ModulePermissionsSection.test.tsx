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
    listUserRolePermissionIds: vi.fn(),
    setUserOverride: vi.fn(),
    removeUserOverride: vi.fn(),
    setModuleOverrides: vi.fn(),
  };
});

vi.mock("@/lib/api/session-policy", () => ({ fetchSessionPolicy: vi.fn() }));

import { getAccessibleModules, getTenantRole } from "@/lib/api/security";
import { fetchSessionPolicy } from "@/lib/api/session-policy";
import { superadminClient } from "@/lib/api/superadmin-client";
import {
  listGrantablePermissions,
  listUserOverrides,
  listUserRolePermissionIds,
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
  vi.mocked(fetchSessionPolicy).mockResolvedValue({ mode: "legacy", maxMinutes: 720 });
  // «Heredado del rol» sale de la API (todos los roles del usuario): p1 lo da el rol.
  vi.mocked(listUserRolePermissionIds).mockResolvedValue({ permissionIds: ["p1"] });
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

  // HU #13653 -----------------------------------------------------------------------------------
  describe("HU #13653", () => {
    const piiModules = [
      ...modules,
      { id: "m3", code: "ict", name: "ICT", sortOrder: 3, actions: [{ id: "p9", slug: "ict.pii.reveal", name: "Revelar datos personales" }] },
    ];
    const piiGrantable = [
      ...grantable.map((g) => (g.id === "p2" ? { ...g, superAdminOnly: true } : g)),
      { id: "p9", slug: "ict.pii.reveal", name: "Revelar datos personales", moduleCode: "ict", productCode: "tramites", superAdminOnly: true },
    ];
    const TIP = "Permiso exclusivo del Super Admin: un administrador de compañía u OT no puede otorgarlo.";
    const PII_TEXT =
      "Este permiso da acceso a datos personales protegidos por la Ley 1581 de 2012. Confirma que el usuario lo requiere para su función.";

    beforeEach(() => {
      vi.mocked(getAccessibleModules).mockResolvedValue(piiModules);
      vi.mocked(listGrantablePermissions).mockResolvedValue(piiGrantable);
    });

    it("AC1: el permiso exclusivo del SA lleva chip «Solo SA», candado y tooltip; el resto no", async () => {
      renderSection();
      const tramites = await screen.findByTestId("module-tramites");
      const crear = within(tramites).getByRole("switch", { name: /Crear trámites/ }).closest("li") as HTMLElement;
      const chip = within(crear).getByTestId("solo-sa");
      expect(chip).toHaveTextContent("Solo SA");
      expect(chip).toHaveAttribute("title", TIP);
      expect(chip.querySelector("svg")).not.toBeNull();
      expect(within(crear).getAllByText(TIP).length).toBeGreaterThan(0);
      const ver = within(tramites).getByRole("switch", { name: /Ver trámites/ }).closest("li") as HTMLElement;
      expect(within(ver).queryByTestId("solo-sa")).not.toBeInTheDocument();
    });

    it("AC2: habilitar ict.pii.reveal abre el diálogo con la advertencia y NO guarda aún", async () => {
      renderSection();
      await userEvent.click(await screen.findByRole("switch", { name: /Revelar datos personales/ }));
      const dialog = await screen.findByRole("dialog");
      expect(within(dialog).getByText(PII_TEXT)).toBeInTheDocument();
      expect(setUserOverride).not.toHaveBeenCalled();
    });

    it("AC2: sin marcar «Confirmo» no se puede pulsar «Otorgar»", async () => {
      renderSection();
      await userEvent.click(await screen.findByRole("switch", { name: /Revelar datos personales/ }));
      const dialog = await screen.findByRole("dialog");
      const otorgar = within(dialog).getByRole("button", { name: "Otorgar" });
      expect(otorgar).toBeDisabled();
      await userEvent.click(otorgar);
      expect(setUserOverride).not.toHaveBeenCalled();
      await userEvent.click(within(dialog).getByLabelText("Confirmo"));
      expect(otorgar).toBeEnabled();
    });

    it("AC2: confirmar guarda con piiConfirmed=true y cierra el diálogo", async () => {
      vi.mocked(setUserOverride).mockResolvedValue({ permissionId: "p9", effect: "allow", changed: true });
      renderSection();
      await userEvent.click(await screen.findByRole("switch", { name: /Revelar datos personales/ }));
      const dialog = await screen.findByRole("dialog");
      await userEvent.click(within(dialog).getByLabelText("Confirmo"));
      await userEvent.click(within(dialog).getByRole("button", { name: "Otorgar" }));
      await waitFor(() => expect(setUserOverride).toHaveBeenCalledWith("u1", "p9", "allow", "t1", true));
      await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    });

    it("AC2: cancelar no cambia nada", async () => {
      renderSection();
      await userEvent.click(await screen.findByRole("switch", { name: /Revelar datos personales/ }));
      const dialog = await screen.findByRole("dialog");
      await userEvent.click(within(dialog).getByLabelText("Confirmo"));
      await userEvent.click(within(dialog).getByRole("button", { name: "Cancelar" }));
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(setUserOverride).not.toHaveBeenCalled();
      expect(screen.getByRole("switch", { name: /Revelar datos personales/ })).toHaveAttribute("aria-checked", "false");
    });

    it("AC2: deshabilitar ict.pii.reveal (ya habilitado) no pide confirmación", async () => {
      vi.mocked(listUserOverrides).mockResolvedValue([
        { permissionId: "p9", slug: "ict.pii.reveal", productCode: "tramites", effect: "allow", source: "INDIVIDUAL", grantedBy: "g", grantedByName: "Ana", grantedAt: null },
      ]);
      vi.mocked(setUserOverride).mockResolvedValue({ permissionId: "p9", effect: "deny", changed: true });
      renderSection();
      await userEvent.click(await screen.findByRole("switch", { name: /Revelar datos personales/ }));
      await waitFor(() => expect(setUserOverride).toHaveBeenCalledWith("u1", "p9", "deny", "t1"));
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });

    it("AC3: al guardar muestra un aviso de éxito con el mensaje de vigencia del usuario", async () => {
      vi.mocked(setUserOverride).mockResolvedValue({ permissionId: "p2", effect: "allow", changed: true });
      renderSection({ userName: "Ana Pérez" });
      await userEvent.click(await screen.findByRole("switch", { name: /Crear trámites/ }));
      expect(
        await screen.findByText("El cambio se aplicará la próxima vez que Ana Pérez inicie sesión (máximo 12 horas)."),
      ).toBeInTheDocument();
      expect(screen.getByRole("status")).toHaveTextContent("Cambio guardado");
    });

    it("AC3: si la API responde error muestra un aviso de error y ninguno de éxito", async () => {
      vi.mocked(setUserOverride).mockRejectedValue(rejected(403, "OUT_OF_CAP"));
      renderSection({ userName: "Ana Pérez" });
      await userEvent.click(await screen.findByRole("switch", { name: /Crear trámites/ }));
      expect(await screen.findByRole("alert")).toHaveTextContent("Ese permiso queda fuera de lo que puedes otorgar.");
      expect(screen.queryByText(/El cambio se aplicará/)).not.toBeInTheDocument();
    });

    it("AC3: si la política de sesión no responde, el guardado igual se informa", async () => {
      vi.mocked(setUserOverride).mockResolvedValue({ permissionId: "p2", effect: "allow", changed: true });
      vi.mocked(fetchSessionPolicy).mockRejectedValue(new Error("boom"));
      renderSection({ userName: "Ana Pérez" });
      await userEvent.click(await screen.findByRole("switch", { name: /Crear trámites/ }));
      expect(await screen.findByText(/El cambio se aplicará la próxima vez que la persona inicie sesión/)).toBeInTheDocument();
    });
  });
});

// HU #13655 (Feature #13104 F-D2) — modo admin: el tope es la lista /grantable.
describe("ModulePermissionsSection — modo admin y tope (#13655)", () => {
  // p1 está dentro del tope (grantable); p2 queda fuera. El usuario tiene p2 por un permiso individual.
  const capOnlyP1 = [grantable[0]];
  const adminProps = { mode: "admin" as const, tenantId: undefined };

  beforeEach(() => {
    vi.mocked(listGrantablePermissions).mockResolvedValue(capOnlyP1);
    vi.mocked(getTenantRole).mockResolvedValue({
      id: "r1", code: "x", name: "Rol", description: null, isSystem: false, isActive: true,
      productCode: "tramites", tenantId: null, permissions: [{ id: "p1", slug: "tramites.view", name: "Ver trámites" }],
    });
  });

  it("AC1: la sección se monta en modo admin con el tope del administrador (sin tenantId en la consulta)", async () => {
    renderSection(adminProps);
    const tramites = await screen.findByTestId("module-tramites");
    expect(within(tramites).getByRole("switch", { name: /Ver trámites/ })).toBeEnabled();
    expect(listGrantablePermissions).toHaveBeenCalledWith(undefined);
    expect(listUserOverrides).toHaveBeenCalledWith("u1", undefined);
  });

  it("AC3: un permiso del usuario fuera del tope lleva el chip «Fuera de tu tope», con tooltip, y su control está deshabilitado", async () => {
    vi.mocked(listUserOverrides).mockResolvedValue([individual]);
    renderSection(adminProps);
    const tramites = await screen.findByTestId("module-tramites");
    const chip = within(tramites).getByTestId("out-of-cap-chip");
    expect(chip).toHaveTextContent("Fuera de tu tope");
    expect(chip).toHaveAttribute("title", "Lo otorgó otro administrador. Puedes quitarlo, pero no volver a otorgarlo.");
    expect(within(tramites).getByRole("tooltip")).toHaveTextContent("Puedes quitarlo, pero no volver a otorgarlo.");
    expect(within(tramites).getByRole("switch", { name: /Crear trámites/ })).toBeDisabled();
    // Pero sí se puede quitar.
    expect(within(tramites).getByRole("button", { name: /Volver Crear trámites a lo que dicta el rol/ })).toBeEnabled();
  });

  it("AC3: tras quitarlo, el permiso sigue visible con el control deshabilitado (no se puede volver a otorgar)", async () => {
    vi.mocked(listUserOverrides).mockResolvedValueOnce([individual]).mockResolvedValue([]);
    vi.mocked(removeUserOverride).mockResolvedValue(undefined);
    renderSection(adminProps);
    await userEvent.click(await screen.findByRole("button", { name: /Volver Crear trámites a lo que dicta el rol/ }));
    await waitFor(() => expect(removeUserOverride).toHaveBeenCalledWith("u1", "p2", undefined));
    const sw = await screen.findByRole("switch", { name: /Crear trámites/ });
    await waitFor(() => expect(sw).toBeDisabled());
    expect(sw).toHaveAttribute("aria-checked", "false");
    expect(screen.queryByRole("button", { name: /Volver Crear trámites/ })).not.toBeInTheDocument();
  });

  it("AC2: sin ninguna acción del módulo en el tope, «Habilitar módulo» se deshabilita y «Denegar módulo» no", async () => {
    vi.mocked(listGrantablePermissions).mockResolvedValue([]);
    vi.mocked(listUserOverrides).mockResolvedValue([individual]);
    renderSection(adminProps);
    const tramites = await screen.findByTestId("module-tramites");
    expect(within(tramites).getByRole("button", { name: /Habilitar el módulo Trámites completo/ })).toBeDisabled();
    expect(within(tramites).getByRole("button", { name: /Denegar el módulo Trámites completo/ })).toBeEnabled();
  });

  it("AC2: «módulo completo» avisa que solo abarcó lo que está dentro del tope", async () => {
    vi.mocked(setModuleOverrides).mockResolvedValue({
      productCode: "tramites", batchId: "b", created: 1, skipped: 0, outOfCap: 1, excluded: 0,
    });
    renderSection(adminProps);
    await userEvent.click(await screen.findByRole("button", { name: /Habilitar el módulo Trámites completo/ }));
    await waitFor(() => expect(setModuleOverrides).toHaveBeenCalledWith("u1", "tramites", "allow", undefined));
    expect(await screen.findByText(/1 acción aplicada; 1 quedaron fuera de lo que puedes otorgar/)).toBeInTheDocument();
  });

  it("en modo superadmin no hay chip de tope", async () => {
    vi.mocked(listUserOverrides).mockResolvedValue([individual]);
    renderSection();
    const tramites = await screen.findByTestId("module-tramites");
    expect(within(tramites).queryByTestId("out-of-cap-chip")).not.toBeInTheDocument();
    expect(within(tramites).getByRole("switch", { name: /Crear trámites/ })).toBeEnabled();
  });
});
