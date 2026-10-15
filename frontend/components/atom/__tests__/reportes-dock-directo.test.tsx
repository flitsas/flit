import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { Shell } from "../Shell";
import { buildDockGroups, DOCK_ITEM_GROUP, SPA_DOCK_ITEM_LABEL } from "../dock/dockGroups";
import { ALL_MODULE_IDS, OT_ADMIN_SPA_OMIT, SPA_DOCK_MODULE_IDS } from "@/lib/nav/modules";

/**
 * HU #13582 (Feature #13579, Épica #13271) — Reportes Detallados sale del dock: el grupo «Reportes»
 * queda con una sola entrada y el dock la pinta como acceso directo (sin submenú intermedio).
 *
 * Adaptación a staging: aquí el dock vive en `Shell.tsx` y navega con `onNav`, no con enlaces.
 *
 * Uso de ejemplo: un usuario con el módulo `reportes` hace clic en «Reportes» del dock y llega al
 * módulo Reportes; la pestaña Reportes Detallados vive dentro del módulo.
 */

vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useRouter: () => ({ push: vi.fn() }),
}));

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

const dock = () => screen.getByRole("navigation", { name: "Navegación principal" });

afterEach(() => {
  window.localStorage.removeItem(TOKEN_STORAGE_KEY);
});

describe("HU #13582 AC1 — Reportes es un acceso directo en el dock", () => {
  it("con el módulo de reportes, «Reportes» navega directo al módulo y no abre submenú", async () => {
    const onNav = vi.fn();
    render(
      <Shell active="dashboard" onNav={onNav} visibleModuleCodes={["tramites", "reportes", "usuarios"]}>
        <div>contenido</div>
      </Shell>,
    );

    const pill = within(dock()).getByRole("button", { name: "Reportes" });
    expect(pill).not.toHaveAttribute("aria-expanded");
    await userEvent.click(pill);
    expect(onNav).toHaveBeenCalledWith("reportes");
  });

  it("contrato: el grupo «reportes» del dock tiene una única entrada sin hijos", () => {
    const entries = ["tramites", "reportes", "usuarios"].map((key) => ({
      key,
      label: key,
      icon: () => null,
      active: false,
      onClick: () => {},
    }));
    const grupo = buildDockGroups(entries as never).find((g) => g.id === "reportes");
    expect(grupo?.items).toHaveLength(1);
    expect(grupo?.items[0]).toEqual(expect.objectContaining({ key: "reportes" }));
    expect(grupo?.items[0].children ?? []).toHaveLength(0);
  });
});

describe("HU #13582 AC2 — la entrada independiente desaparece del dock", () => {
  it("aunque el RBAC devuelva el código viejo, el dock no muestra «Reportes Detallados»", () => {
    render(
      <Shell
        active="dashboard"
        onNav={vi.fn()}
        visibleModuleCodes={["tramites", "reportes", "reportes-detallados", "validaciones", "usuarios"]}
      >
        <div>contenido</div>
      </Shell>,
    );
    expect(within(dock()).queryByText("Reportes Detallados")).not.toBeInTheDocument();
    expect(within(dock()).queryByRole("button", { name: "Reportes Detallados" })).not.toBeInTheDocument();
  });

  it("Super Admin: el dock pintado no muestra «Reportes Detallados»", () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken({ sub: "u1", role: "SuperAdmin", email: "demo@flit.local" }));
    render(
      <Shell active="dashboard" onNav={vi.fn()} visibleModuleCodes={["tramites", "reportes", "validaciones", "usuarios"]}>
        <div>contenido</div>
      </Shell>,
    );
    expect(within(dock()).queryByText("Reportes Detallados")).not.toBeInTheDocument();
  });

  it("contrato: el id ya no existe en los catálogos de módulos SPA ni en el registro del dock", () => {
    expect(ALL_MODULE_IDS).not.toContain("reportes-detallados");
    expect(SPA_DOCK_MODULE_IDS).not.toContain("reportes-detallados");
    expect(OT_ADMIN_SPA_OMIT.has("reportes-detallados")).toBe(false);
    expect(Object.keys(SPA_DOCK_ITEM_LABEL)).not.toContain("reportes-detallados");
    expect(Object.keys(DOCK_ITEM_GROUP)).not.toContain("reportes-detallados");
  });
});
