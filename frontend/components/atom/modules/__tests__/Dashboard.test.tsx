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
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type { ActiveModulesResponse, AnalyticsOverviewResponse } from "@/lib/api/types";

// ── Mocks compartidos de la capa de datos y de identidad (sin red real) ─────
const mocks = vi.hoisted(() => ({
  fetchAnalyticsOverview: vi.fn(),
  fetchMonthlyTrend: vi.fn(),
  fetchActiveModules: vi.fn(),
  fetchAllCompanies: vi.fn(),
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
vi.mock("@/lib/api/admin-companies", () => ({ fetchAllCompanies: mocks.fetchAllCompanies }));
vi.mock("@/lib/api/tramites-client", () => ({
  ALL_TENANTS: "*",
  tramitesClient: { listTenantBiometricValidations: mocks.listTenantBiometricValidations },
}));
vi.mock("@/lib/api/client", () => ({ getToken: mocks.getToken }));
// HU #12364: el Dashboard monta `useNetworkScope` → `usePermissions`, que lee más helpers del
// JWT; se conservan los reales y solo se sustituyen los dos que estos tests gobiernan.
vi.mock("@/lib/auth/jwt", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/auth/jwt")>()),
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
    { category: "matriculas", total: 5, byStatus: [{ status: "aprobado", count: 5 }] },
    { category: "traspasos", total: 2, byStatus: [{ status: "entregado", count: 2 }] },
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
    mocks.decodeJwtPayload.mockReturnValue({ display_name: "Ana", email: "ana@flit.io", permissions: ["dashboard.read"] });
    mocks.isSuperAdmin.mockReturnValue(false);
    mocks.fetchAnalyticsOverview.mockResolvedValue(FULL_OVERVIEW);
    mocks.fetchMonthlyTrend.mockResolvedValue(TREND);
    mocks.listTenantBiometricValidations.mockResolvedValue(BIOMETRIC_EMPTY);
    mocks.fetchActiveModules.mockResolvedValue(NONE_ADDITIONAL);
    mocks.fetchAllCompanies.mockResolvedValue([]);
    mocks.getActiveBanners.mockResolvedValue([]);
  });

  it("AC1: TramitesModuleEnabled=true muestra la sección de Trámites igual que hoy (KPIs, distribución, tendencia)", async () => {
    render(<Dashboard onNewTramite={noop} />);

    await waitFor(() => expect(mocks.fetchActiveModules).toHaveBeenCalled());
    expect(await screen.findByText("Total trámites")).toBeInTheDocument();
    // "Matrículas"/"Traspasos" también aparecen en la leyenda del gráfico de tendencia.
    expect(screen.getAllByText("Matrículas").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Traspasos").length).toBeGreaterThan(0);
    expect(screen.getByText("Completados")).toBeInTheDocument();
    expect(await screen.findByText("Distribución General de Trámites")).toBeInTheDocument();
    expect(await screen.findByText("Seguimiento operativo")).toBeInTheDocument();

    // Sin ningún módulo adicional activado (estado por defecto), la sección "Próximamente"
    // no ocupa espacio: ni tarjeta, ni mensaje de estado vacío — la compañía no contrató
    // Comparendos ni Resoluciones y no hay nada que anunciar.
    await waitFor(() => expect(mocks.fetchActiveModules).toHaveBeenCalled());
    expect(screen.queryByText("Próximamente")).not.toBeInTheDocument();
    expect(screen.queryByText("Tu compañía no tiene módulos adicionales activados.")).not.toBeInTheDocument();
  });

  it("BUG12588 / HU #12725 D3: «Otros» ya no es KPI aparte (absorbido en Total) y capitaliza estados en Distribución General", async () => {
    render(<Dashboard onNewTramite={noop} />);

    await waitFor(() => expect(mocks.fetchActiveModules).toHaveBeenCalled());
    expect(screen.queryByText("Otros Trámites")).not.toBeInTheDocument();
    expect(await screen.findByText("Distribución General de Trámites")).toBeInTheDocument();
    // Labels de negocio capitalizados (estadoLabel), no los códigos crudos que persiste la BD.
    expect(await screen.findByText("Aprobado")).toBeInTheDocument();
    expect(screen.getByText("Entregado")).toBeInTheDocument();
    expect(screen.queryByText("aprobado")).not.toBeInTheDocument();
    expect(screen.queryByText("entregado")).not.toBeInTheDocument();
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
    expect(await screen.findByText("Total trámites")).toBeInTheDocument();
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

  it("HU #12711 — sin permiso de Validaciones ni dashboard.read, la tarjeta de validaciones no se pide ni se pinta", async () => {
    mocks.decodeJwtPayload.mockReturnValue({ display_name: "Rada", email: "rada@flit.io", permissions: ["tramites.read"] });

    render(<Dashboard onNewTramite={noop} />);

    expect(await screen.findByText("Total trámites")).toBeInTheDocument();
    await waitFor(() => expect(mocks.fetchAnalyticsOverview).toHaveBeenCalled());
    expect(mocks.listTenantBiometricValidations).not.toHaveBeenCalled();
    expect(screen.queryByText("Validaciones Biométricas")).not.toBeInTheDocument();
    expect(screen.queryByText("No se pudieron cargar las métricas del dashboard.")).not.toBeInTheDocument();
  });

  it("HU #12711 — un usuario de organismo no ve la tarjeta aunque tenga dashboard.read", async () => {
    mocks.decodeJwtPayload.mockReturnValue({
      display_name: "Ot",
      email: "ot@flit.io",
      entity_type: "TRANSIT_OFFICE",
      permissions: ["dashboard.read"],
    });

    render(<Dashboard onNewTramite={noop} />);

    expect(await screen.findByText("Total trámites")).toBeInTheDocument();
    await waitFor(() => expect(mocks.fetchAnalyticsOverview).toHaveBeenCalled());
    expect(mocks.listTenantBiometricValidations).not.toHaveBeenCalled();
  });

  it("SuperAdmin en 'Todas las compañías': las validaciones se piden de todas (ALL_TENANTS) y no queda en error permanente", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    mocks.fetchAllCompanies.mockResolvedValue([]);

    render(<Dashboard onNewTramite={noop} />);

    expect(await screen.findByText("Total trámites")).toBeInTheDocument();
    await waitFor(() => expect(mocks.fetchAnalyticsOverview).toHaveBeenCalled());

    // Para este endpoint (sin vista global) un tenantId vacío respondería 400 del backend —
    // antes de este fix eso dejaba la fila de "Próximamente" en error permanente sin importar
    // qué se cambiara en configuración de compañía. Ahora, sin una compañía concreta elegida,
    // la sección completa no ocupa espacio: ni tarjeta, ni mensaje, ni alerta de error.
    await waitFor(() => expect(mocks.fetchAllCompanies).toHaveBeenCalled());
    // HU #12706 (AC4) — el listado plano ya tiene vista global: se pide sin compañía, nunca con el
    // tenant del JWT del SuperAdmin. (El primer render, antes de saber que es SuperAdmin, lanza una
    // carga que se aborta; cuentan las dos últimas: stats y por vencer.)
    await waitFor(() => {
      const calls = mocks.listTenantBiometricValidations.mock.calls;
      expect(calls.length).toBeGreaterThanOrEqual(2);
      expect(calls.slice(-2).map((c) => c[1])).toEqual(["*", "*"]);
    });
    expect(screen.queryByText("Selecciona una compañía para ver sus módulos activos.")).not.toBeInTheDocument();
    expect(screen.queryByText("Próximamente")).not.toBeInTheDocument();
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
    mocks.decodeJwtPayload.mockReturnValue({ display_name: "Ana", email: "ana@flit.io", permissions: ["dashboard.read"] });
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
    // Bug #12584 defecto 3: sin banners (un solo slide navegable), Anterior/Siguiente deben
    // quedar deshabilitados — no hay a dónde moverse.
    expect(screen.getByRole("button", { name: "Anterior" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Siguiente" })).toBeDisabled();
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
    expect(screen.getByRole("button", { name: "Anterior" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Siguiente" })).toBeEnabled();

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
    expect(await screen.findByText("Total trámites")).toBeInTheDocument();
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

// ── Cuerpo dinámico del slide de bienvenida (Opción A: sin llamadas nuevas) ─────────────────

const GENERIC_BODY =
  "Tus procesos y validaciones se encuentran sincronizados. Continúa gestionando tu operación de manera segura y eficiente.";

/** Mock de `listTenantBiometricValidations` que distingue la consulta de stats (sin
 *  `vigenciaEstado`) de la de "por vencer" (`vigenciaEstado: 'por_vencer'`), como hace el
 *  componente real con dos llamadas al mismo cliente. */
function mockBiometricCalls(rechazadas: number, porVencer: number) {
  mocks.listTenantBiometricValidations.mockImplementation(async (params: Record<string, unknown>) => {
    if (params?.vigenciaEstado === "por_vencer") {
      return { validations: [], stats: { total: 0, aprobadas: 0, enProceso: 0, rechazadas: 0, expiradas: 0 }, page: 1, pageSize: 10, total: porVencer };
    }
    return {
      validations: [],
      stats: { total: rechazadas, aprobadas: 0, enProceso: 0, rechazadas, expiradas: 0 },
      page: 1,
      pageSize: 10,
      total: rechazadas,
    };
  });
}

describe("Dashboard — cuerpo dinámico del slide de bienvenida (Opción A)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getToken.mockReturnValue("token");
    mocks.decodeJwtPayload.mockReturnValue({ display_name: "Ana", email: "ana@flit.io", permissions: ["dashboard.read"] });
    mocks.isSuperAdmin.mockReturnValue(false);
    mocks.fetchMonthlyTrend.mockResolvedValue(CAROUSEL_TREND);
    mocks.fetchActiveModules.mockResolvedValue(NONE_ADDITIONAL);
    mocks.fetchAllCompanies.mockResolvedValue([]);
    mocks.getActiveBanners.mockResolvedValue([]);
  });

  it("con rechazadas y por vencer, combina ambas en un solo mensaje", async () => {
    mocks.fetchAnalyticsOverview.mockResolvedValue(OVERVIEW);
    mockBiometricCalls(2, 1);
    render(<Dashboard onNewTramite={vi.fn()} />);

    expect(
      await screen.findByText(
        "Tienes 3 validaciones de identidad que requieren tu atención: 2 rechazadas y 1 por vencer.",
      ),
    ).toBeInTheDocument();
  });

  it("solo con rechazadas (singular), usa el mensaje en singular", async () => {
    mocks.fetchAnalyticsOverview.mockResolvedValue(OVERVIEW);
    mockBiometricCalls(1, 0);
    render(<Dashboard onNewTramite={vi.fn()} />);

    expect(
      await screen.findByText(
        "Tienes 1 validación de identidad rechazada que requiere tu atención.",
      ),
    ).toBeInTheDocument();
  });

  it("solo con validaciones por vencer (plural), usa ese mensaje", async () => {
    mocks.fetchAnalyticsOverview.mockResolvedValue(OVERVIEW);
    mockBiometricCalls(0, 3);
    render(<Dashboard onNewTramite={vi.fn()} />);

    expect(
      await screen.findByText(
        "Tienes 3 validaciones de identidad por vencer que requieren tu atención.",
      ),
    ).toBeInTheDocument();
  });

  it("sin alertas, cae al total de trámites del periodo (dato ya cargado, sin llamada nueva)", async () => {
    mocks.fetchAnalyticsOverview.mockResolvedValue(FULL_OVERVIEW);
    mockBiometricCalls(0, 0);
    render(<Dashboard onNewTramite={vi.fn()} />);

    expect(
      await screen.findByText(
        "Tienes 7 trámites en este periodo. Todo sincronizado — sin identidades pendientes de atención.",
      ),
    ).toBeInTheDocument();
  });

  it("mientras las biométricas cargan, mantiene el texto genérico (sin parpadeo de '0 alertas')", async () => {
    mocks.fetchAnalyticsOverview.mockResolvedValue(OVERVIEW);
    // Nunca resuelve dentro de este test: el estado se queda en "loading".
    mocks.listTenantBiometricValidations.mockReturnValue(new Promise(() => {}));
    render(<Dashboard onNewTramite={vi.fn()} />);

    expect(await screen.findByText(GENERIC_BODY)).toBeInTheDocument();
  });
});

// ── BUG #12588 (defecto 4) — el dashboard arranca SIN rango de fechas ────────
//
// Antes partía del mes en curso (`defaultRange`, AC2 de la HU #10247). Como el backend acota por
// fecha de CREACIÓN, todo lo radicado antes quedaba fuera de las tarjetas aunque siguiera en curso,
// y QA lo reportó como un total mal calculado. No era un error de conteo: era otro universo.
describe("Dashboard — BUG #12588 rango de fechas por defecto", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getToken.mockReturnValue("token");
    mocks.decodeJwtPayload.mockReturnValue({ display_name: "Ana", email: "ana@flit.io", permissions: ["dashboard.read"] });
    mocks.isSuperAdmin.mockReturnValue(false);
    mocks.fetchAnalyticsOverview.mockResolvedValue(FULL_OVERVIEW);
    mocks.fetchMonthlyTrend.mockResolvedValue(TREND);
    mocks.listTenantBiometricValidations.mockResolvedValue(BIOMETRIC_EMPTY);
    mocks.fetchActiveModules.mockResolvedValue(NONE_ADDITIONAL);
    mocks.fetchAllCompanies.mockResolvedValue([]);
    mocks.getActiveBanners.mockResolvedValue([]);
  });

  it("al abrir, pide el overview sin from ni to (universo completo)", async () => {
    render(<Dashboard onNewTramite={noop} />);

    await waitFor(() => expect(mocks.fetchAnalyticsOverview).toHaveBeenCalled());
    const [params] = mocks.fetchAnalyticsOverview.mock.calls[0];
    expect(params.from).toBeFalsy();
    expect(params.to).toBeFalsy();
  });

  it("al abrir, las estadísticas biométricas tampoco se acotan por fecha", async () => {
    render(<Dashboard onNewTramite={noop} />);

    await waitFor(() => expect(mocks.listTenantBiometricValidations).toHaveBeenCalled());
    const [params] = mocks.listTenantBiometricValidations.mock.calls[0];
    expect(params.createdFrom).toBeUndefined();
    expect(params.createdTo).toBeUndefined();
  });

  it("el selector de rango arranca vacío (HU #12724)", async () => {
    render(<Dashboard onNewTramite={noop} />);

    expect(
      await screen.findByRole("button", { name: /Rango de fechas: Seleccionar rango/i }),
    ).toBeInTheDocument();
  });

  it("al aplicar una sola fecha, sí se acota: el filtro sigue disponible", async () => {
    const user = userEvent.setup();
    render(<Dashboard onNewTramite={noop} />);
    await waitFor(() => expect(mocks.fetchAnalyticsOverview).toHaveBeenCalled());

    await user.click(screen.getByRole("button", { name: /Rango de fechas/i }));
    const dialog = await screen.findByRole("dialog", { name: "Elegir rango de fechas" });
    await user.click(within(dialog).getByTestId("day-2026-09-01"));
    await user.click(within(dialog).getByTestId("date-range-apply"));

    await waitFor(() => {
      const ultima = mocks.fetchAnalyticsOverview.mock.calls.at(-1)![0];
      expect(ultima.from).toBe("2026-09-01");
    });
    // Un solo extremo NO es un rango a medio llenar: acota por ese lado y el otro queda abierto.
    expect(mocks.fetchAnalyticsOverview.mock.calls.at(-1)![0].to).toBeFalsy();
  });

  it("Limpiar en el picker devuelve a la vista sin acotar (HU #12724 / #12725)", async () => {
    const user = userEvent.setup();
    render(<Dashboard onNewTramite={noop} />);

    await user.click(screen.getByRole("button", { name: /Rango de fechas/i }));
    let dialog = await screen.findByRole("dialog", { name: "Elegir rango de fechas" });
    await user.click(within(dialog).getByTestId("day-2026-09-01"));
    await user.click(within(dialog).getByTestId("date-range-apply"));
    await waitFor(() => {
      expect(mocks.fetchAnalyticsOverview.mock.calls.at(-1)![0].from).toBe("2026-09-01");
    });

    await user.click(screen.getByRole("button", { name: /Rango de fechas/i }));
    dialog = await screen.findByRole("dialog", { name: "Elegir rango de fechas" });
    await user.click(within(dialog).getByTestId("date-range-clear"));

    expect(
      await screen.findByRole("button", { name: /Rango de fechas: Seleccionar rango/i }),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(mocks.fetchAnalyticsOverview.mock.calls.at(-1)![0].from).toBeFalsy();
    });
  });
});

// ── Tarjetas KPI: el rótulo se lee entero ────────────────────────────────────
//
// El título compartía fila con el icono dentro de un `min-w-0` con `truncate`, así que solo
// disponía de `ancho − 48px` y en pantalla se veían «Total Trá…», «Otros Trá…» y «Completa…».
// Ahora ocupa la fila completa y el icono baja a la del número.
describe("Dashboard — rótulos de las tarjetas KPI", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getToken.mockReturnValue("token");
    mocks.decodeJwtPayload.mockReturnValue({ display_name: "Ana", email: "ana@flit.io", permissions: ["dashboard.read"] });
    mocks.isSuperAdmin.mockReturnValue(false);
    mocks.fetchAnalyticsOverview.mockResolvedValue(FULL_OVERVIEW);
    mocks.fetchMonthlyTrend.mockResolvedValue(TREND);
    mocks.listTenantBiometricValidations.mockResolvedValue(BIOMETRIC_EMPTY);
    mocks.fetchActiveModules.mockResolvedValue(NONE_ADDITIONAL);
    mocks.fetchAllCompanies.mockResolvedValue([]);
    mocks.getActiveBanners.mockResolvedValue([]);
  });

  it.each(["Total trámites", "Matrículas", "Traspasos", "Completados"])(
    "«%s» se renderiza completo, sin recortar",
    async (label) => {
      render(<Dashboard onNewTramite={noop} />);

      const rotulo = await screen.findByText(label);
      expect(rotulo).toBeInTheDocument();
      // `truncate` corta por CSS sin tocar el texto, así que el nodo tiene que llevar el
      // tratamiento de dos líneas y NO la clase que recortaba.
      expect(rotulo).toHaveClass("line-clamp-2");
      expect(rotulo).not.toHaveClass("truncate");
    },
  );

  it("el rótulo respeta el piso tipográfico de 12px de la línea base", async () => {
    render(<Dashboard onNewTramite={noop} />);

    const rotulo = await screen.findByText("Total trámites");
    expect(rotulo).toHaveClass("text-xs");
    expect(rotulo.className).not.toMatch(/text-\[1[01]px\]/);
  });

  it("la cifra no se trunca: recortarla mostraría un conteo falso", async () => {
    render(<Dashboard onNewTramite={noop} />);

    const cifra = await screen.findByText("7");
    expect(cifra).toHaveClass("tabular-nums");
    expect(cifra).not.toHaveClass("truncate");
  });
});

// ── HU #12725 — banner alto estándar, filtros en fila, KPIs 2×2 (B.1 / D3) ───

describe("Dashboard — HU #12725 layout hero y KPIs 2×2", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getToken.mockReturnValue("token");
    mocks.decodeJwtPayload.mockReturnValue({ display_name: "Ana", email: "ana@flit.io", permissions: ["dashboard.read"] });
    mocks.isSuperAdmin.mockReturnValue(false);
    mocks.fetchAnalyticsOverview.mockResolvedValue(FULL_OVERVIEW);
    mocks.fetchMonthlyTrend.mockResolvedValue(TREND);
    mocks.listTenantBiometricValidations.mockResolvedValue(BIOMETRIC_EMPTY);
    mocks.fetchActiveModules.mockResolvedValue(NONE_ADDITIONAL);
    mocks.fetchAllCompanies.mockResolvedValue([]);
    mocks.getActiveBanners.mockResolvedValue([]);
  });

  it("AC1 — banner y columna derecha comparten min-height del token --dashboard-hero-h", async () => {
    render(<Dashboard onNewTramite={noop} />);

    const banner = await screen.findByTestId("dashboard-hero-banner");
    const column = screen.getByTestId("dashboard-hero-column");
    expect(banner.className).toContain("min-h-[var(--dashboard-hero-h)]");
    expect(column.className).toContain("min-h-[var(--dashboard-hero-h)]");
    expect(banner.style.minHeight).toBe("");
  });

  it("AC2 — SuperAdmin ve DateRangePicker y CompanySelector en la misma fila de filtros", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    mocks.fetchAllCompanies.mockResolvedValue([
      { id: "c1", razonSocial: "Acme SA", nit: "900" },
    ]);

    render(<Dashboard onNewTramite={noop} />);

    const filterRow = await screen.findByTestId("dashboard-filter-row");
    expect(within(filterRow).getByTestId("date-range-picker")).toBeInTheDocument();
    expect(within(filterRow).getByRole("combobox", { name: /Compañía/i })).toBeInTheDocument();
    expect(filterRow.className).toMatch(/grid-cols-2/);
  });

  it("AC3 — KPIs en grilla 2×2 (Total, Matrículas, Traspasos, Completados); sin «Otros Trámites»", async () => {
    render(<Dashboard onNewTramite={noop} />);

    const grid = await screen.findByTestId("dashboard-kpi-grid");
    expect(grid.className).toContain("grid-cols-2");
    expect(within(grid).getByText("Total trámites")).toBeInTheDocument();
    expect(within(grid).getByText("Matrículas")).toBeInTheDocument();
    expect(within(grid).getByText("Traspasos")).toBeInTheDocument();
    expect(within(grid).getByText("Completados")).toBeInTheDocument();
    expect(within(grid).queryByText("Otros Trámites")).not.toBeInTheDocument();
    // Total incluye todas las categorías (5+2+0 = 7 en FULL_OVERVIEW).
    expect(within(grid).getByText("7")).toBeInTheDocument();
  });

  it("AC4 — el slide de bienvenida recorta título y cuerpo con line-clamp", async () => {
    render(<Dashboard onNewTramite={noop} />);

    const banner = await screen.findByTestId("dashboard-hero-banner");
    const title = within(banner).getByRole("heading", { name: /Hola,/i });
    expect(title).toHaveClass("line-clamp-2");
    const body = within(banner).getByText(/Tienes 7 trámites|Tus procesos y validaciones/i);
    expect(body).toHaveClass("line-clamp-3");
  });

  it("AC5 — el token --dashboard-hero-h está definido en globals.css (:root)", () => {
    const css = readFileSync(resolve(__dirname, "../../../../app/globals.css"), "utf8");
    expect(css).toMatch(/--dashboard-hero-h:\s*260px/);
  });
});
