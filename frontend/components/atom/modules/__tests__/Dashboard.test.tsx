// Tests de HU #12253 (Feature #12249) y HU #12242 (Feature #12236) para Dashboard.tsx.
//
// HU #12253 — Cubre AC1-AC5: visibilidad condicional de la sección de Trámites y tarjetas
// "Próximamente" para Comparendos/Resoluciones, según los flags que expone
// GET /api/v1/analytics/active-modules. Redefinición post-validación en vivo con el usuario
// (2026-09-10): la tarjeta "Próximamente" avisa de un módulo que la compañía SÍ activó
// (`...ModuleEnabled = true`) pero que todavía no tiene contenido real construido — NO al
// revés. Si el flag está apagado, la compañía no lo contrató y no se le menciona. Los AC de
// ADO se actualizaron para reflejar esto.
//
// HU #12242 — el carrusel de bienvenida del gestor deja de anunciar mensajes de relleno
// hardcodeados y pasa a mostrar, después del slide fijo, los banners Activos que configura el
// Administrador (endpoint público `GET /api/v1/public/banners/active`).
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type { ActiveModulesResponse, AnalyticsOverviewResponse } from "@/lib/api/types";

// ── Mocks compartidos de la capa de datos y de identidad (sin red real) ─────
const mocks = vi.hoisted(() => ({
  fetchAnalyticsOverview: vi.fn(),
  fetchMonthlyTrend: vi.fn(),
  fetchActiveModules: vi.fn(),
  fetchCompaniesIndex: vi.fn(),
  listTenantBiometricValidations: vi.fn(),
  getToken: vi.fn(),
  decodeJwtPayload: vi.fn(),
  isSuperAdmin: vi.fn(),
  getActiveBanners: vi.fn(),
}));

vi.mock("@/lib/api/analytics", () => ({
  fetchAnalyticsOverview: mocks.fetchAnalyticsOverview,
  fetchMonthlyTrend: mocks.fetchMonthlyTrend,
  fetchActiveModules: mocks.fetchActiveModules,
}));
vi.mock("@/lib/api/admin-companies", () => ({ fetchCompaniesIndex: mocks.fetchCompaniesIndex }));
vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: { listTenantBiometricValidations: mocks.listTenantBiometricValidations },
}));
vi.mock("@/lib/api/client", () => ({ getToken: mocks.getToken }));
vi.mock("@/lib/auth/jwt", () => ({
  decodeJwtPayload: mocks.decodeJwtPayload,
  isSuperAdmin: mocks.isSuperAdmin,
}));
vi.mock("@/lib/api/public-banners", () => ({
  getActiveBanners: mocks.getActiveBanners,
  bannerImageUrl: (id: string) => `http://api.test/api/v1/public/banners/${id}/image`,
}));

import { Dashboard } from "@/components/atom/modules/Dashboard";

function noop() {}

// ── HU #12253 — módulos activos por tenant ───────────────────────────────────

const FULL_OVERVIEW: AnalyticsOverviewResponse = {
  tenantId: "11111111-1111-1111-1111-111111111111",
  from: "2026-08-01",
  to: "2026-08-31",
  categories: [
    { category: "matriculas", total: 5, byStatus: [{ status: "completed", count: 5 }] },
    { category: "traspasos", total: 2, byStatus: [{ status: "submitted", count: 2 }] },
    { category: "otros", total: 0, byStatus: [] },
  ],
};

const TREND = { items: [{ year: 2026, month: 8, category: "matriculas" as const, total: 5 }] };

/** Estado por defecto real de una compañía nueva (TenantSettings.Default, HU #12250). */
const NONE_ADDITIONAL: ActiveModulesResponse = {
  tramitesModuleEnabled: true,
  comparendosModuleEnabled: false,
  resolucionesModuleEnabled: false,
};

const ALL_ENABLED: ActiveModulesResponse = {
  tramitesModuleEnabled: true,
  comparendosModuleEnabled: true,
  resolucionesModuleEnabled: true,
};

const BIOMETRIC_EMPTY = {
  validations: [],
  stats: { total: 0, aprobadas: 0, enProceso: 0, rechazadas: 0, expiradas: 0 },
  page: 1,
  pageSize: 10,
  total: 0,
};

describe("Dashboard — HU #12253 módulos activos por tenant", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getToken.mockReturnValue("token");
    mocks.decodeJwtPayload.mockReturnValue({ display_name: "Ana", email: "ana@flit.io" });
    mocks.isSuperAdmin.mockReturnValue(false);
    mocks.fetchAnalyticsOverview.mockResolvedValue(FULL_OVERVIEW);
    mocks.fetchMonthlyTrend.mockResolvedValue(TREND);
    mocks.listTenantBiometricValidations.mockResolvedValue(BIOMETRIC_EMPTY);
    mocks.fetchActiveModules.mockResolvedValue(NONE_ADDITIONAL);
    mocks.getActiveBanners.mockResolvedValue([]);
  });

  it("AC1: TramitesModuleEnabled=true muestra la sección de Trámites igual que hoy (KPIs, distribución, tendencia)", async () => {
    render(<Dashboard onNewTramite={noop} />);

    await waitFor(() => expect(mocks.fetchActiveModules).toHaveBeenCalled());
    expect(await screen.findByText("Total Trámites")).toBeInTheDocument();
    // "Matrículas"/"Traspasos" también aparecen en la leyenda del gráfico de tendencia.
    expect(screen.getAllByText("Matrículas").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Traspasos").length).toBeGreaterThan(0);
    expect(screen.getByText("Completados")).toBeInTheDocument();
    expect(await screen.findByText("Distribución General de Trámites")).toBeInTheDocument();
    expect(await screen.findByText("Seguimiento operativo")).toBeInTheDocument();

    // Sin ningún módulo adicional activado (estado por defecto), no debe verse ninguna
    // tarjeta "Próximamente" — la compañía no contrató Comparendos ni Resoluciones.
    expect(screen.queryByText("Próximamente")).not.toBeInTheDocument();
    expect(await screen.findByText("Tu compañía no tiene módulos adicionales activados.")).toBeInTheDocument();
  });

  it("AC2: ComparendosModuleEnabled=true muestra la tarjeta Próximamente con el estilo de las tarjetas KPI existentes", async () => {
    mocks.fetchActiveModules.mockResolvedValue({
      tramitesModuleEnabled: true,
      comparendosModuleEnabled: true,
      resolucionesModuleEnabled: false,
    });

    render(<Dashboard onNewTramite={noop} />);

    const label = await screen.findByText("Comparendos");
    expect(screen.getByText("Próximamente")).toBeInTheDocument();

    const card = label.closest("div.rounded-2xl");
    expect(card).not.toBeNull();
    expect(card?.className).toContain("bg-white");
    expect(card?.className).toContain("dark:bg-[#0B0F14]");
    expect(card?.className).toContain("border");

    // Resoluciones sigue apagado (la compañía no lo activó): no debe verse su placeholder.
    expect(screen.queryByText("Resoluciones")).not.toBeInTheDocument();
  });

  it("AC3: ResolucionesModuleEnabled=true muestra su propia tarjeta Próximamente, independiente de Comparendos", async () => {
    mocks.fetchActiveModules.mockResolvedValue({
      tramitesModuleEnabled: true,
      comparendosModuleEnabled: false,
      resolucionesModuleEnabled: true,
    });

    render(<Dashboard onNewTramite={noop} />);

    expect(await screen.findByText("Resoluciones")).toBeInTheDocument();
    expect(screen.getByText("Próximamente")).toBeInTheDocument();
    expect(screen.queryByText("Comparendos")).not.toBeInTheDocument();
  });

  it("Comparendos y Resoluciones activos a la vez muestran las DOS tarjetas, cada una independiente", async () => {
    mocks.fetchActiveModules.mockResolvedValue(ALL_ENABLED);

    render(<Dashboard onNewTramite={noop} />);

    expect(await screen.findByText("Comparendos")).toBeInTheDocument();
    expect(await screen.findByText("Resoluciones")).toBeInTheDocument();
    expect(screen.getAllByText("Próximamente")).toHaveLength(2);
  });

  it("AC4: al recargar el Dashboard con el flag recién activado, aparece la tarjeta Próximamente de Comparendos (no había nada antes)", async () => {
    mocks.fetchActiveModules.mockResolvedValueOnce(NONE_ADDITIONAL);
    const { unmount } = render(<Dashboard onNewTramite={noop} />);
    await waitFor(() => expect(mocks.fetchActiveModules).toHaveBeenCalledTimes(1));
    expect(screen.queryByText("Comparendos")).not.toBeInTheDocument();
    unmount();

    // Simula la "recarga" del Dashboard (nuevo montaje) tras activar el flag desde Admin.
    mocks.fetchActiveModules.mockResolvedValueOnce({
      tramitesModuleEnabled: true,
      comparendosModuleEnabled: true,
      resolucionesModuleEnabled: false,
    });
    render(<Dashboard onNewTramite={noop} />);

    await waitFor(() => expect(mocks.fetchActiveModules).toHaveBeenCalledTimes(2));
    expect(await screen.findByText("Comparendos")).toBeInTheDocument();
    expect(screen.getByText("Próximamente")).toBeInTheDocument();
    // Explícitamente fuera de alcance: no se construye contenido real del módulo.
    expect(screen.queryByTestId("comparendos-module-content")).not.toBeInTheDocument();
  });

  it("AC5: si falla la consulta de módulos activos, el error queda aislado y no tumba el resto del dashboard", async () => {
    mocks.fetchActiveModules.mockRejectedValue(new Error("network down"));

    render(<Dashboard onNewTramite={noop} />);

    // El resto de secciones (independientes) sigue operativo.
    expect(await screen.findByText("Total Trámites")).toBeInTheDocument();
    expect(await screen.findByText("Distribución General de Trámites")).toBeInTheDocument();
    expect(await screen.findByText("Validaciones Biométricas")).toBeInTheDocument();
    expect(await screen.findByText("Seguimiento operativo")).toBeInTheDocument();

    // El fallo se refleja de forma aislada (UiStateBoundary) donde irían las tarjetas
    // "Próximamente", sin propagarse a las demás secciones.
    await waitFor(() => expect(mocks.fetchActiveModules).toHaveBeenCalled());
    const alerts = await screen.findAllByRole("alert");
    expect(alerts).toHaveLength(1);
    expect(screen.queryByText("Comparendos")).not.toBeInTheDocument();
    expect(screen.queryByText("Resoluciones")).not.toBeInTheDocument();
  });

  it("SuperAdmin en 'Todas las compañías': no llama al endpoint (no hay un tenant concreto) y no queda en error permanente", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    mocks.fetchCompaniesIndex.mockResolvedValue({ data: [], total: 0, page: 1, pageSize: 100 });

    render(<Dashboard onNewTramite={noop} />);

    expect(await screen.findByText("Total Trámites")).toBeInTheDocument();
    await waitFor(() => expect(mocks.fetchAnalyticsOverview).toHaveBeenCalled());

    // Para este endpoint (sin vista global) un tenantId vacío respondería 400 del backend —
    // antes de este fix eso dejaba la fila de "Próximamente" en error permanente sin importar
    // qué se cambiara en configuración de compañía. Se explica con un mensaje, no con la
    // alerta roja de error genérico.
    expect(
      await screen.findByText("Selecciona una compañía para ver sus módulos activos."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});

// ── HU #12242 — carrusel de bienvenida con banners Activos ──────────────────

const OVERVIEW = { categories: [] };
const CAROUSEL_TREND = { items: [] };
const BIOMETRICS = {
  validations: [],
  stats: { total: 0, aprobadas: 0, enProceso: 0, rechazadas: 0, expiradas: 0 },
  page: 1,
  pageSize: 20,
  total: 0,
};

function banner(overrides: Partial<{ id: string; name: string; linkUrl: string | null }> = {}) {
  return { id: "banner-1", name: "Novedad de la plataforma", linkUrl: null, ...overrides };
}

describe("Dashboard — carrusel de bienvenida con banners Activos (HU #12242)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getToken.mockReturnValue("token");
    mocks.decodeJwtPayload.mockReturnValue({ display_name: "Ana", email: "ana@flit.io" });
    mocks.isSuperAdmin.mockReturnValue(false);
    mocks.fetchAnalyticsOverview.mockResolvedValue(OVERVIEW);
    mocks.fetchMonthlyTrend.mockResolvedValue(CAROUSEL_TREND);
    mocks.listTenantBiometricValidations.mockResolvedValue(BIOMETRICS);
    // Módulos activos: irrelevante para estos tests del carrusel — se deja en el default
    // (solo Trámites) para que no aparezcan tarjetas "Próximamente" de ruido.
    mocks.fetchActiveModules.mockResolvedValue(NONE_ADDITIONAL);
  });

  it("AC1 — sin banners activos, el carrusel solo muestra el slide fijo de bienvenida", async () => {
    mocks.getActiveBanners.mockResolvedValue([]);
    render(<Dashboard onNewTramite={vi.fn()} />);

    await waitFor(() => expect(mocks.getActiveBanners).toHaveBeenCalled());
    // Un solo punto de navegación: no hay más slides detrás del fijo.
    expect(screen.getAllByRole("button", { name: /^Slide \d/ })).toHaveLength(1);
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  it("AC1 — los banners activos se agregan como slides después del fijo", async () => {
    mocks.getActiveBanners.mockResolvedValue([
      banner({ id: "b1", name: "Banner uno" }),
      banner({ id: "b2", name: "Banner dos", linkUrl: "https://flit.example/novedad" }),
    ]);
    render(<Dashboard onNewTramite={vi.fn()} />);

    await waitFor(() => expect(mocks.getActiveBanners).toHaveBeenCalled());
    await waitFor(() =>
      expect(screen.getAllByRole("button", { name: /^Slide \d/ })).toHaveLength(3),
    );

    // Avanza al primer banner (slide 2 de 3): sin enlace, no hay ningún <a> envolviendo el banner
    // ni título visible — el nombre solo viaja como texto accesible de la imagen (alt).
    await userEvent.click(screen.getByRole("button", { name: "Siguiente" }));
    const primerBannerImg = await screen.findByRole("img", { name: "Banner uno" });
    expect(primerBannerImg).toHaveAttribute(
      "src",
      "http://api.test/api/v1/public/banners/b1/image",
    );
    expect(screen.queryByRole("link", { name: "Banner uno" })).not.toBeInTheDocument();
    // El nombre sigue presente para lectores de pantalla (sr-only), pero no como texto visible.
    expect(screen.getByText("Banner uno")).toHaveClass("sr-only");

    // Avanza al segundo banner, que sí tiene enlace: el enlace cubre todo el banner (clic en
    // cualquier punto navega), sin ningún título visible — el nombre es el nombre accesible
    // (aria-label) del propio <a>, no texto en pantalla.
    await userEvent.click(screen.getByRole("button", { name: "Siguiente" }));
    expect(await screen.findByRole("img", { name: "Banner dos" })).toBeInTheDocument();
    const enlace = screen.getByRole("link", { name: "Banner dos" });
    expect(enlace).toHaveAttribute("href", "https://flit.example/novedad");
    expect(enlace).toHaveAttribute("target", "_blank");
    expect(enlace).toHaveAttribute("rel", "noopener noreferrer");
    expect(enlace.textContent).toBe("");
  });

  it("AC3 — un fallo al consultar banners degrada al slide fijo, sin romper el dashboard", async () => {
    mocks.getActiveBanners.mockRejectedValue(new Error("network error"));
    render(<Dashboard onNewTramite={vi.fn()} />);

    await waitFor(() => expect(mocks.getActiveBanners).toHaveBeenCalled());
    expect(screen.getAllByRole("button", { name: /^Slide \d/ })).toHaveLength(1);
    // El resto del dashboard (KPIs) se sigue viendo con normalidad.
    expect(await screen.findByText("Total Trámites")).toBeInTheDocument();
  });

  it("AC3 — si la imagen de un banner no carga, ese slide se retira sin romper el carrusel", async () => {
    mocks.getActiveBanners.mockResolvedValue([banner({ id: "roto", name: "Banner roto" })]);
    render(<Dashboard onNewTramite={vi.fn()} />);

    await waitFor(() =>
      expect(screen.getAllByRole("button", { name: /^Slide \d/ })).toHaveLength(2),
    );
    await userEvent.click(screen.getByRole("button", { name: "Siguiente" }));
    const img = await screen.findByRole("img", { name: "Banner roto" });

    img.dispatchEvent(new Event("error"));

    await waitFor(() =>
      expect(screen.getAllByRole("button", { name: /^Slide \d/ })).toHaveLength(1),
    );
    expect(screen.queryByRole("img", { name: "Banner roto" })).not.toBeInTheDocument();
  });
});
