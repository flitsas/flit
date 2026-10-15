import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { Shell } from "../Shell";
import { tramitesNav, type TramitesNavContext } from "../dock/tramitesNav";
import { DOCK_ITEM_GROUP, SPA_DOCK_ITEM_LABEL } from "../dock/dockGroups";
import { ALL_MODULE_IDS, OT_ADMIN_SPA_OMIT, SPA_DOCK_MODULE_IDS } from "@/lib/nav/modules";
import type { NavItem } from "@flit/shell/nav";

/**
 * HU #13582 (Feature #13579, Épica #13271) — Reportes Detallados sale del dock: el grupo «Reportes»
 * queda con una sola entrada y el dock la pinta como enlace directo (sin submenú intermedio).
 *
 * Uso de ejemplo: un usuario con el módulo `reportes` hace clic en «Reportes» del dock y llega a
 * `/?m=reportes`; la pestaña Reportes Detallados vive dentro del módulo.
 */

const nav = vi.hoisted(() => ({ pathname: "/" }));
vi.mock("next/navigation", () => ({
  usePathname: () => nav.pathname,
  useRouter: () => ({ push: vi.fn() }),
}));

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

const dock = () => screen.getByRole("navigation", { name: "Navegación principal" });

function flatten(items: readonly NavItem[]): NavItem[] {
  return items.flatMap((i) => [i, ...flatten(i.children ?? [])]);
}

const CTX: TramitesNavContext = {
  isOtUser: false,
  isOtAdmin: false,
  isAdminCompany: false,
  isGroupParent: false,
  tenantId: "t1",
  otTransitOfficeId: null,
};

beforeEach(() => {
  nav.pathname = "/";
});

afterEach(() => {
  window.localStorage.removeItem(TOKEN_STORAGE_KEY);
});

describe("HU #13582 AC1 — Reportes es un enlace directo en el dock", () => {
  it("con el módulo de reportes, «Reportes» lleva directo a /?m=reportes y no abre submenú", () => {
    render(
      <Shell visibleModuleCodes={["tramites", "reportes", "usuarios"]} search="">
        <div>contenido</div>
      </Shell>,
    );

    const link = within(dock()).getByRole("link", { name: "Reportes" });
    expect(link).toHaveAttribute("href", "/?m=reportes");
    expect(within(dock()).queryByRole("button", { name: /Reportes/ })).not.toBeInTheDocument();
  });

  it("contrato: el grupo «reportes» del catálogo tiene una única entrada sin hijos", () => {
    const enReportes = tramitesNav(CTX).items.filter((i) => i.section === "reportes");
    expect(enReportes).toHaveLength(1);
    expect(enReportes[0]).toEqual(expect.objectContaining({ key: "reportes", href: "/?m=reportes" }));
    expect(enReportes[0].children ?? []).toHaveLength(0);
  });
});

describe("HU #13582 AC2 — la entrada independiente desaparece del dock", () => {
  it.each([
    ["Gestor", CTX],
    ["Admin de Compañía cabeza de red", { ...CTX, isAdminCompany: true, isGroupParent: true }],
    ["Usuario de Organismo de Tránsito", { ...CTX, isOtUser: true, isOtAdmin: true, otTransitOfficeId: "ot-1" }],
  ])("%s: ninguna entrada del catálogo apunta a Reportes Detallados", (_perfil, ctx) => {
    const all = flatten(tramitesNav(ctx).items);
    expect(all.map((i) => i.key)).not.toContain("reportes-detallados");
    expect(all.map((i) => i.label)).not.toContain("Reportes Detallados");
    expect(all.some((i) => i.href?.includes("reportes-detallados"))).toBe(false);
  });

  it("Super Admin: el dock pintado no muestra «Reportes Detallados»", () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken({ sub: "u1", role: "SuperAdmin", email: "demo@flit.local" }));
    render(
      <Shell visibleModuleCodes={["tramites", "reportes", "validaciones", "usuarios"]} search="">
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
