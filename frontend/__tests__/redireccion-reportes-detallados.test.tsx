import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";

/**
 * HU #13582 (Feature #13579, Épica #13271) — los enlaces antiguos `/?m=reportes-detallados` llevan a la
 * pestaña Reportes Detallados del módulo Reportes, y el manual / Dr. FLIT la ubican ahí.
 *
 * Uso de ejemplo: un marcador guardado a `/?m=reportes-detallados` hace `router.replace` a
 * `/?m=reportes&reportesTab=detallados` antes de evaluar permisos; desde ahí decide el gate normal.
 */

const h = vi.hoisted(() => ({
  search: "m=reportes-detallados",
  replace: vi.fn(),
  push: vi.fn(),
  codes: ["reportes"] as string[],
  authed: true,
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: h.replace, push: h.push }),
  useSearchParams: () => new URLSearchParams(h.search),
  usePathname: () => "/",
}));
vi.mock("@/hooks/useAuthGate", () => ({
  useAuthGate: () => ({ authed: h.authed, hydrated: true, logout: vi.fn() }),
}));
vi.mock("@/hooks/useAccessibleModules", () => ({
  useAccessibleModules: () => ({
    modules: h.codes.map((code) => ({ code })),
    loading: false,
    ready: true,
    error: null,
  }),
}));
vi.mock("@/lib/api/client", () => ({ getToken: () => null }));
vi.mock("@/lib/auth/jwt", () => ({
  decodeJwtPayload: () => null,
  isSuperAdmin: () => false,
  isOtUser: () => false,
  canReadLogQx: () => false,
  canReadIctLogs: () => false,
  canReadIctReportes: () => false,
  canReadIctTrazabilidad: () => false,
}));
vi.mock("@/lib/telemetry", () => ({ trackModuleView: vi.fn() }));
vi.mock("@/components/atom/Shell", () => ({
  Shell: ({ children }: { children: React.ReactNode }) => <div data-testid="shell">{children}</div>,
}));
vi.mock("@/components/atom/modules/Reportes", () => ({ Reportes: () => <div data-testid="modulo-reportes" /> }));
vi.mock("@/components/atom/modules/Dashboard", () => ({ Dashboard: () => <div data-testid="modulo-dashboard" /> }));
vi.mock("@/components/atom/modules/OtDashboard", () => ({ OtDashboard: () => null }));
vi.mock("@/components/atom/modules/Tramites", () => ({ Tramites: () => null }));
vi.mock("@/components/atom/modules/Validaciones", () => ({ Validaciones: () => null }));
vi.mock("@/components/atom/modules/HistorialPlaca", () => ({ HistorialPlaca: () => null }));
vi.mock("@/components/atom/modules/Usuarios", () => ({ Usuarios: () => null }));
vi.mock("@/components/atom/modules/Ayuda", () => ({ Ayuda: () => null }));
vi.mock("@/components/atom/modules/RbacAdmin", () => ({ RbacAdmin: () => null }));
vi.mock("@/components/atom/modules/Auditoria", () => ({ Auditoria: () => null }));
vi.mock("@/components/atom/modules/LogQx", () => ({ LogQx: () => null }));
vi.mock("@/components/atom/modules/IctLogs", () => ({ IctLogs: () => null }));
vi.mock("@/components/atom/modules/IctReports", () => ({ IctReports: () => null }));
vi.mock("@/components/atom/modules/IctTrazabilidad", () => ({ IctTrazabilidad: () => null }));

import HomePage from "@/app/page";
import { legacyModuleRedirect, planSpaModuleAccess, resolveNavigableModuleIds } from "@/lib/nav/modules";
import { resolveContextArticle } from "@/lib/manual/context";
import { searchManualArticles, getArticleBySlug } from "@/lib/manual/catalog";
import { MANUAL_MODULE_ARTICLES } from "@/lib/manual/articles/meta";
import { resolveAffectedModule } from "@/components/dr-flit/dr-flit-support-case";

const NUEVA = "/?m=reportes&reportesTab=detallados";

beforeEach(() => {
  vi.clearAllMocks();
  h.search = "m=reportes-detallados";
  h.codes = ["reportes"];
  h.authed = true;
});

describe("HU #13582 AC3 — los enlaces antiguos redirigen a la pestaña", () => {
  it("/?m=reportes-detallados hace replace a la pestaña, sin el aviso de módulo no autorizado", async () => {
    render(<HomePage />);

    await waitFor(() => expect(h.replace).toHaveBeenCalledWith(NUEVA, { scroll: false }));
    expect(h.replace).toHaveBeenCalledTimes(1);
    expect(screen.queryByText("No tienes acceso a ese módulo.")).not.toBeInTheDocument();
  });

  it("sin sesión no redirige: deja que el control de sesión lleve al login (no queda en blanco)", async () => {
    h.authed = false;
    render(<HomePage />);

    await new Promise((r) => setTimeout(r, 50));
    expect(h.replace).not.toHaveBeenCalledWith(NUEVA, expect.anything());
  });

  it("ya en la dirección nueva, quien tiene Reportes ve el módulo", async () => {
    h.search = "m=reportes&reportesTab=detallados";
    render(<HomePage />);

    expect(await screen.findByTestId("modulo-reportes")).toBeInTheDocument();
    expect(h.replace).not.toHaveBeenCalled();
  });

  it("contrato: solo el id retirado tiene redirección", () => {
    expect(legacyModuleRedirect("reportes-detallados")).toBe(NUEVA);
    expect(legacyModuleRedirect("reportes")).toBeNull();
    expect(legacyModuleRedirect(null)).toBeNull();
  });
});

describe("HU #13582 AC4 — enlace antiguo sin permiso (negativo)", () => {
  it("sin acceso a Reportes: tras redirigir recibe el aviso estándar y vuelve al inicio, sin error", async () => {
    h.codes = ["tramites"];
    const { rerender } = render(<HomePage />);
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith(NUEVA, { scroll: false }));

    // El router aplica el replace: la página se vuelve a pintar con la dirección nueva.
    h.search = "m=reportes&reportesTab=detallados";
    rerender(<HomePage />);

    expect(await screen.findByText("No tienes acceso a ese módulo.")).toBeInTheDocument();
    await waitFor(() => expect(h.replace).toHaveBeenLastCalledWith("/?m=dashboard", { scroll: false }));
    expect(screen.queryByTestId("modulo-reportes")).not.toBeInTheDocument();
  });

  it("contrato: el gate deniega `reportes` a quien no tiene el módulo", () => {
    const ids = resolveNavigableModuleIds({
      accessibleCodes: ["tramites"],
      isSuperAdmin: false,
      isOtAdmin: false,
      canReadLogQx: false,
      canReadIctLogs: false,
      canReadIctTrazabilidad: false,
      canReadIctReportes: false,
    });
    expect(planSpaModuleAccess("reportes", ids)).toEqual(expect.objectContaining({ denied: true, replaceTo: "/?m=dashboard" }));
  });
});

describe("HU #13582 AC5 — manual y Dr. FLIT al día", () => {
  it("buscar «reportes detallados» lleva a artículos que la ubican como pestaña de Reportes", () => {
    const hits = searchManualArticles("reportes detallados", 8, {});
    const slugs = hits.map((h) => h.slug);
    expect(slugs).toContain("1-gestor/14-reportes-detallados");

    const texto = JSON.stringify(getArticleBySlug("1-gestor/14-reportes-detallados"));
    expect(texto).toContain("pestaña del módulo Reportes");
    expect(texto).not.toMatch(/píldora propia del dock|módulo aparte/);
    expect(JSON.stringify(getArticleBySlug("1-gestor/11-reportes"))).toContain("Pestaña Reportes Detallados");
  });

  it("Dr. FLIT sugiere el artículo de Reportes en el módulo y ya no conoce el id retirado", () => {
    expect(resolveContextArticle("/|reportes")?.slug).toBe("1-gestor/11-reportes");
    expect(MANUAL_MODULE_ARTICLES).not.toHaveProperty("reportes-detallados");
    expect(resolveAffectedModule("/|reportes")).toBe("Reportes");
  });
});
