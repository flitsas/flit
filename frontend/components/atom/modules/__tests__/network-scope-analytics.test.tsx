import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type { AnalyticsOverviewResponse } from "@/lib/api/types";
import type { DetailedReportPage, NetworkDetailedReportPage } from "@/lib/api/detailed-report";

/**
 * HU #12364 — Vista consolidada: alcance de red en estadísticas y reportes (Feature #12257).
 *
 * Uso de ejemplo: una cabeza de red abre el Dashboard o el reporte detallado; ve el MISMO selector
 * «Alcance» del módulo de trámites (`NetworkScopeSelector`, preferencia `tramites.scope`). Con la
 * red activa las consultas van a `/api/v1/tramites/network/stats/*` y
 * `/api/v1/tramites/network/reports/procedures[/export]`, cada indicador lleva el distintivo «Red»
 * y la exportación usa exactamente los filtros de la consulta mostrada. Un cliente sin jerarquía
 * no ve nada de esto y sus llamadas son las de siempre.
 *
 * AC1 — estadísticas de la red en el Dashboard: rutas `network/stats/*` + distintivo por indicador.
 * AC2 — reporte detallado por hijo o agregado; el selector solo ofrece hijos de la red.
 * AC3 — la exportación viaja con los mismos filtros y alcance que la consulta mostrada.
 * AC4 — usuario sin jerarquía: sin selector y llamadas idénticas a hoy.
 * AC5 — un solo control (`NetworkScopeSelector`) y una sola preferencia (`tramites.scope`).
 * AC6 — en alcance de red no se ofrece descarga de documentos ni anexos.
 */

const mocks = vi.hoisted(() => ({
  fetchAnalyticsOverview: vi.fn(),
  fetchMonthlyTrend: vi.fn(),
  fetchTopProducers: vi.fn(),
  fetchActiveModules: vi.fn(),
  fetchNetworkAnalyticsOverview: vi.fn(),
  fetchNetworkMonthlyTrend: vi.fn(),
  fetchNetworkTopProducers: vi.fn(),
  exportAnalyticsExcel: vi.fn(),
  exportExecutivePdf: vi.fn(),
  fetchDetailedReport: vi.fn(),
  exportDetailedReport: vi.fn(),
  fetchNetworkDetailedReport: vi.fn(),
  exportNetworkDetailedReport: vi.fn(),
  fetchAllCompanies: vi.fn(),
  fetchCompanyChildren: vi.fn(),
  fetchNetworkChildren: vi.fn(),
  listTenantBiometricValidations: vi.fn(),
  listPublishedProcedureTypes: vi.fn(),
  listTransitOffices: vi.fn(),
  getActiveBanners: vi.fn(),
  usePermissions: vi.fn(),
  prefsGet: vi.fn(),
  prefsPut: vi.fn(),
  fetchLiveOverview: vi.fn(),
  fetchOtMetrics: vi.fn(),
  fetchFunnel: vi.fn(),
  fetchUsageMetrics: vi.fn(),
}));

vi.mock("@/lib/api/analytics", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/analytics")>()),
  fetchAnalyticsOverview: mocks.fetchAnalyticsOverview,
  fetchMonthlyTrend: mocks.fetchMonthlyTrend,
  fetchTopProducers: mocks.fetchTopProducers,
  fetchActiveModules: mocks.fetchActiveModules,
  fetchNetworkAnalyticsOverview: mocks.fetchNetworkAnalyticsOverview,
  fetchNetworkMonthlyTrend: mocks.fetchNetworkMonthlyTrend,
  fetchNetworkTopProducers: mocks.fetchNetworkTopProducers,
  exportAnalyticsExcel: mocks.exportAnalyticsExcel,
  exportExecutivePdf: mocks.exportExecutivePdf,
}));
vi.mock("@/lib/api/analytics-v2", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/analytics-v2")>()),
  fetchLiveOverview: mocks.fetchLiveOverview,
  fetchOtMetrics: mocks.fetchOtMetrics,
  fetchFunnel: mocks.fetchFunnel,
  fetchUsageMetrics: mocks.fetchUsageMetrics,
}));
vi.mock("@/lib/api/detailed-report", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/detailed-report")>()),
  fetchDetailedReport: mocks.fetchDetailedReport,
  exportDetailedReport: mocks.exportDetailedReport,
  fetchNetworkDetailedReport: mocks.fetchNetworkDetailedReport,
  exportNetworkDetailedReport: mocks.exportNetworkDetailedReport,
}));
vi.mock("@/lib/api/admin-companies", () => ({
  fetchAllCompanies: mocks.fetchAllCompanies,
  fetchCompanyChildren: mocks.fetchCompanyChildren,
}));
vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    listTenantBiometricValidations: mocks.listTenantBiometricValidations,
    listPublishedProcedureTypes: mocks.listPublishedProcedureTypes,
    listTransitOffices: mocks.listTransitOffices,
  },
  fetchNetworkChildren: mocks.fetchNetworkChildren,
  TramitesApiError: class TramitesApiError extends Error {
    status: number;
    problem: Record<string, unknown> | null;
    constructor(status: number, message: string, problem: Record<string, unknown> | null = null) {
      super(message);
      this.name = "TramitesApiError";
      this.status = status;
      this.problem = problem;
    }
  },
}));
vi.mock("@/lib/api/ui-preferences", () => ({
  uiPreferencesClient: { get: mocks.prefsGet, put: mocks.prefsPut },
}));
vi.mock("@/lib/api/public-banners", () => ({
  getActiveBanners: mocks.getActiveBanners,
  bannerImageUrl: (id: string) => `http://api.test/api/v1/public/banners/${id}/image`,
}));
vi.mock("@/hooks/usePermissions", () => ({ usePermissions: mocks.usePermissions }));

import { Dashboard } from "@/components/atom/modules/Dashboard";
import { Reportes } from "@/components/atom/modules/Reportes";
import { DetailedReportPanel } from "@/components/atom/modules/_reportesDetallados/DetailedReportPanel";
import { NetworkScopeSelector } from "@/components/operacion/NetworkScopeSelector";
import { ETIQUETA_ALCANCE, ETIQUETA_ALCANCE_RED, ETIQUETA_DISTINTIVO_RED } from "@/lib/tramites/network-scope";

const CABEZA = "11111111-1111-1111-1111-111111111111";
const HIJO = "22222222-2222-2222-2222-222222222222";
const HIJO_2 = "33333333-3333-3333-3333-333333333333";
const AJENO = "99999999-9999-9999-9999-999999999999";
const SCOPE_KEY = "tramites.scope";

function permisos(over: Record<string, unknown> = {}) {
  return {
    permissions: ["reportes.detallados.export", "reportes.resumen.read", "reportes.operacion.read"],
    isSuperAdmin: false,
    isAdminCompany: true,
    isOtAdmin: false,
    isGroupParent: false,
    hasParentTenant: false,
    parentTenantId: null,
    tenantId: CABEZA,
    userId: "user-1",
    roleId: "r1",
    roleCode: "AdminCompany",
    ...over,
  };
}

// HU #12555/#12556 — el endpoint no-admin (`/api/v1/tramites/network/children`) ya entrega id+nombre.
const HIJOS = [
  { id: HIJO, nombre: "Concesionario Hijo SAS" },
  { id: HIJO_2, nombre: "Agencia Norte SAS" },
];

const OVERVIEW: AnalyticsOverviewResponse = {
  tenantId: CABEZA,
  from: "2026-09-01",
  to: "2026-09-30",
  categories: [
    { category: "matriculas", total: 5, byStatus: [{ status: "completed", count: 5 }] },
    { category: "traspasos", total: 2, byStatus: [{ status: "submitted", count: 2 }] },
    { category: "otros", total: 0, byStatus: [] },
  ],
};
const NETWORK_OVERVIEW = { ...OVERVIEW, categories: [{ category: "matriculas" as const, total: 40, byStatus: [{ status: "completed", count: 40 }] }], scope: { tenantIds: [CABEZA, HIJO, HIJO_2] } };
const TREND = { items: [{ year: 2026, month: 8, category: "matriculas" as const, total: 5 }] };

const FILA = {
  id: "p1",
  referenceNumber: "TR-1",
  procedureTypeName: "Traspaso",
  category: "traspasos",
  status: "entregado",
  createdByDisplayName: "Gestor",
  submittedAt: null,
  completedAt: null,
  personDocument: "123",
  personFullName: "Persona",
  hasTransformation: false,
  transformationDetail: null,
  isLeasing: false,
  paymentType: "",
  transferType: "Normal",
};
const PAGINA_PROPIA: DetailedReportPage = {
  items: [FILA],
  totalCount: 1,
  page: 1,
  pageSize: 20,
  summary: { totalCount: 1, byStatus: [], byCategory: [], byProcedureType: [] },
};
const PAGINA_RED: NetworkDetailedReportPage = {
  ...PAGINA_PROPIA,
  items: [{ ...FILA, tenantId: HIJO, tenantName: "Concesionario Hijo SAS" }],
};

function cabeza(scope: unknown = { mode: "network" }) {
  mocks.usePermissions.mockReturnValue(permisos({ isGroupParent: true }));
  mocks.prefsGet.mockResolvedValue({ value: scope });
  mocks.fetchNetworkChildren.mockResolvedValue(HIJOS);
}

function sinJerarquia() {
  mocks.usePermissions.mockReturnValue(permisos());
}

beforeEach(() => {
  vi.clearAllMocks();
  // Reportes persiste la pestaña en `?reportesTab=`: cada test arranca en la primera.
  window.history.replaceState({}, "", "/");
  mocks.fetchAnalyticsOverview.mockResolvedValue(OVERVIEW);
  mocks.fetchMonthlyTrend.mockResolvedValue(TREND);
  mocks.fetchTopProducers.mockResolvedValue({ items: [] });
  mocks.fetchNetworkAnalyticsOverview.mockResolvedValue(NETWORK_OVERVIEW);
  mocks.fetchNetworkMonthlyTrend.mockResolvedValue({ ...TREND, scope: { tenantIds: [CABEZA, HIJO] } });
  mocks.fetchNetworkTopProducers.mockResolvedValue({ items: [], scope: { tenantIds: [CABEZA] } });
  mocks.fetchActiveModules.mockResolvedValue({ tramitesModuleEnabled: true, comparendosModuleEnabled: false, resolucionesModuleEnabled: false });
  mocks.fetchAllCompanies.mockResolvedValue([]);
  mocks.listTenantBiometricValidations.mockResolvedValue({ stats: { total: 0, aprobadas: 0, enProceso: 0, rechazadas: 0 }, total: 0, items: [] });
  mocks.listPublishedProcedureTypes.mockResolvedValue([]);
  mocks.listTransitOffices.mockResolvedValue([]);
  mocks.getActiveBanners.mockResolvedValue([]);
  mocks.fetchDetailedReport.mockResolvedValue(PAGINA_PROPIA);
  mocks.fetchNetworkDetailedReport.mockResolvedValue(PAGINA_RED);
  mocks.exportDetailedReport.mockResolvedValue(undefined);
  mocks.exportNetworkDetailedReport.mockResolvedValue(undefined);
  mocks.prefsPut.mockResolvedValue(undefined);
  mocks.fetchLiveOverview.mockResolvedValue({ generatedAt: "", today: { creados: 0, byStatus: [], entregados: 0, aprobados: 0, rechazados: 0 }, stuckCount: 0, pendingIdentityValidations: 0, integrationsLastHour: { calls: 0, errors: 0, avgDurationMs: 0 }, lastActivityAt: null });
  mocks.fetchFunnel.mockResolvedValue({ steps: [] });
  mocks.fetchOtMetrics.mockResolvedValue({ items: [] });
  mocks.fetchUsageMetrics.mockResolvedValue({ items: [] });
});

// ── AC1 — estadísticas de la red en el Dashboard ─────────────────────────────

describe("HU #12364 AC1 — estadísticas de la red en la pantalla de analítica", () => {
  it("con alcance «Toda la red» las consultas van a network/stats/* y cada indicador lleva el distintivo «Red»", async () => {
    cabeza({ mode: "network" });
    render(<Dashboard onNewTramite={() => {}} />);

    await waitFor(() => expect(mocks.fetchNetworkAnalyticsOverview).toHaveBeenCalled());
    const [params] = mocks.fetchNetworkAnalyticsOverview.mock.calls[0];
    expect(params.childTenantId).toBeUndefined();
    expect(params).not.toHaveProperty("tenantId");
    expect(mocks.fetchNetworkMonthlyTrend).toHaveBeenCalled();
    // Contrato: las rutas propias no se tocan con la red activa.
    expect(mocks.fetchAnalyticsOverview).not.toHaveBeenCalled();
    expect(mocks.fetchMonthlyTrend).not.toHaveBeenCalled();

    // El agregado de la red (40) sustituye al propio (5) en el KPI.
    expect((await screen.findAllByText("40")).length).toBeGreaterThanOrEqual(1);
    // Distintivo textual en los 4 KPIs y en las dos secciones de trámites.
    const badges = await screen.findAllByRole("status", { name: new RegExp(`^${ETIQUETA_DISTINTIVO_RED}:`) });
    expect(badges.length).toBeGreaterThanOrEqual(6);
    expect(screen.getByTestId("kpi-red-Total Trámites")).toHaveTextContent(ETIQUETA_DISTINTIVO_RED);
    // Lo que no tiene ruta de red (biometría) se rotula como propio, no como red.
    expect(screen.getByTestId("biometria-solo-propia")).toBeInTheDocument();
  });

  it("con un cliente hijo elegido manda childTenantId y el distintivo nombra al hijo", async () => {
    cabeza({ mode: "network", childTenantId: HIJO });
    render(<Dashboard onNewTramite={() => {}} />);

    await waitFor(() => expect(mocks.fetchNetworkAnalyticsOverview).toHaveBeenCalled());
    // La lista de hijos llega después de la preferencia: el hijo sigue siendo válido.
    await waitFor(() =>
      expect(mocks.fetchNetworkAnalyticsOverview).toHaveBeenLastCalledWith(
        expect.objectContaining({ childTenantId: HIJO }),
        expect.anything(),
      ),
    );
    expect(mocks.fetchNetworkMonthlyTrend).toHaveBeenLastCalledWith(
      expect.objectContaining({ childTenantId: HIJO }),
      expect.anything(),
    );
    const kpi = await screen.findByTestId("kpi-red-Total Trámites");
    await waitFor(() => expect(kpi).toHaveTextContent("Concesionario Hijo SAS"));
  });

  it("con alcance propio la cabeza hace las llamadas de siempre y sin distintivo", async () => {
    cabeza({ mode: "own" });
    render(<Dashboard onNewTramite={() => {}} />);

    await waitFor(() => expect(mocks.fetchAnalyticsOverview).toHaveBeenCalled());
    expect(mocks.fetchAnalyticsOverview).toHaveBeenCalledWith(
      expect.objectContaining({ tenantId: undefined }),
      expect.anything(),
    );
    expect(mocks.fetchNetworkAnalyticsOverview).not.toHaveBeenCalled();
    expect(screen.queryByTestId("kpi-red-Total Trámites")).not.toBeInTheDocument();
    // Pero sí ve el selector para poder cambiar de alcance.
    expect(screen.getByTestId("dashboard-network-scope-select")).toBeInTheDocument();
  });
});

// ── AC2 — reporte por hijo o agregado; el selector solo ofrece hijos de la red ─

describe("HU #12364 AC2 — reporte filtrado por cliente hijo o agregado", () => {
  it("con un hijo elegido consulta network/reports/procedures con childTenantId, sin tenantId, y pinta la columna «Cliente»", async () => {
    cabeza({ mode: "network", childTenantId: HIJO });
    render(<DetailedReportPanel />);

    await waitFor(() =>
      expect(mocks.fetchNetworkDetailedReport).toHaveBeenLastCalledWith(
        expect.objectContaining({ childTenantId: HIJO, page: 1, pageSize: 20 }),
        expect.anything(),
      ),
    );
    const [params] = mocks.fetchNetworkDetailedReport.mock.calls.at(-1)!;
    expect(params).not.toHaveProperty("tenantId");
    expect(mocks.fetchDetailedReport).not.toHaveBeenCalled();

    expect(await screen.findByRole("columnheader", { name: "Cliente" })).toBeInTheDocument();
    expect(screen.getByTestId("detallado-fila-cliente")).toHaveTextContent("Concesionario Hijo SAS");
    expect(screen.getByTestId("detallado-red-badge")).toHaveTextContent("Concesionario Hijo SAS");
  });

  it("con el agregado de la red no manda childTenantId", async () => {
    cabeza({ mode: "network" });
    render(<DetailedReportPanel />);
    await waitFor(() => expect(mocks.fetchNetworkDetailedReport).toHaveBeenCalled());
    const [params] = mocks.fetchNetworkDetailedReport.mock.calls.at(-1)!;
    expect(params.childTenantId).toBeUndefined();
  });

  it("el selector solo ofrece «Mi compañía», «Toda la red» y los hijos de la red — ningún cliente ajeno", async () => {
    cabeza({ mode: "network" });
    render(<DetailedReportPanel />);
    const select = await screen.findByTestId("detallado-network-scope-select");
    await waitFor(() => expect(within(select).getAllByRole("option").length).toBe(4));
    const valores = within(select).getAllByRole("option").map((o) => (o as HTMLOptionElement).value);
    expect(valores).toEqual(["own", "network", `child:${HIJO_2}`, `child:${HIJO}`].sort((a, b) => valores.indexOf(a) - valores.indexOf(b)));
    expect(valores.some((v) => v.includes(AJENO))).toBe(false);
    // Orden por nombre (Agencia Norte antes que Concesionario Hijo).
    expect(valores[2]).toBe(`child:${HIJO_2}`);
  });
});

// ── AC3 — la exportación respeta alcance y filtro ────────────────────────────

describe("HU #12364 AC3 — la exportación respeta el alcance y el filtro", () => {
  it("exporta con exactamente los mismos filtros y alcance de la consulta mostrada", async () => {
    cabeza({ mode: "network", childTenantId: HIJO });
    const user = userEvent.setup();
    render(<DetailedReportPanel />);
    await waitFor(() => expect(mocks.fetchNetworkDetailedReport).toHaveBeenCalled());

    // Aplica un filtro (estado) y consulta.
    await user.selectOptions(screen.getByLabelText("Estado"), "entregado");
    await user.type(screen.getByLabelText("Nombre persona"), "Pérez");
    await user.click(screen.getByRole("button", { name: "Buscar" }));
    await waitFor(() =>
      expect(mocks.fetchNetworkDetailedReport).toHaveBeenLastCalledWith(
        expect.objectContaining({ status: "entregado", personName: "Pérez", childTenantId: HIJO }),
        expect.anything(),
      ),
    );
    const [consulta] = mocks.fetchNetworkDetailedReport.mock.calls.at(-1)!;

    await user.click(screen.getByRole("button", { name: /Descargar Excel/ }));
    await waitFor(() => expect(mocks.exportNetworkDetailedReport).toHaveBeenCalledTimes(1));
    const [exportado] = mocks.exportNetworkDetailedReport.mock.calls[0];
    // Mismos filtros y alcance que la consulta (la paginación no forma parte del archivo).
    const { page: _p, pageSize: _s, ...filtrosConsulta } = consulta;
    const { page: _p2, pageSize: _s2, ...filtrosExport } = exportado;
    expect(filtrosExport).toEqual(filtrosConsulta);
    expect(exportado).not.toHaveProperty("tenantId");
    // Nunca la exportación propia con datos de la red.
    expect(mocks.exportDetailedReport).not.toHaveBeenCalled();
  });

  it("en Reportes 2.0 los exports analíticos (Excel/PDF) no se ofrecen con la red activa; pestañas sin ruta de red avisan", async () => {
    cabeza({ mode: "network" });
    const user = userEvent.setup();
    render(<Reportes />);

    await waitFor(() => expect(mocks.fetchNetworkAnalyticsOverview).toHaveBeenCalled());
    expect(mocks.fetchAnalyticsOverview).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: /Exportar Excel/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Resumen Ejecutivo PDF/ })).not.toBeInTheDocument();
    expect(screen.getByTestId("reportes-export-no-disponible-red")).toHaveTextContent(/solo para tu compañía/i);
    expect(screen.getByTestId("reportes-red-badge")).toHaveTextContent(ETIQUETA_DISTINTIVO_RED);
    // El panel «Ahora mismo» no tiene ruta de red: no se consulta lo propio bajo rótulo de red.
    expect(mocks.fetchLiveOverview).not.toHaveBeenCalled();

    await user.click(screen.getByRole("tab", { name: /Operación/ }));
    expect(await screen.findByTestId("reportes-tab-no-disponible-red")).toBeInTheDocument();
    expect(mocks.fetchFunnel).not.toHaveBeenCalled();
  });
});

// ── AC4 — un cliente sin jerarquía no percibe cambios ─────────────────────────

describe("HU #12364 AC4 — un cliente sin jerarquía no percibe cambios", () => {
  it("Dashboard: sin selector, sin preferencia consultada y llamadas idénticas a hoy", async () => {
    sinJerarquia();
    render(<Dashboard onNewTramite={() => {}} />);

    await waitFor(() => expect(mocks.fetchAnalyticsOverview).toHaveBeenCalledTimes(1));
    expect(mocks.fetchAnalyticsOverview).toHaveBeenCalledWith(
      { from: expect.any(String), to: expect.any(String), tenantId: undefined },
      expect.anything(),
    );
    expect(mocks.fetchMonthlyTrend).toHaveBeenCalledTimes(1);
    expect(mocks.fetchNetworkAnalyticsOverview).not.toHaveBeenCalled();
    expect(mocks.fetchNetworkMonthlyTrend).not.toHaveBeenCalled();
    expect(mocks.prefsGet).not.toHaveBeenCalled();
    expect(mocks.fetchNetworkChildren).not.toHaveBeenCalled();
    expect(screen.queryByLabelText(ETIQUETA_ALCANCE)).not.toBeInTheDocument();
    expect(screen.queryByTestId("kpi-red-Total Trámites")).not.toBeInTheDocument();
  });

  it("Reporte detallado: sin selector, consulta y exportación propias", async () => {
    sinJerarquia();
    const user = userEvent.setup();
    render(<DetailedReportPanel />);

    await waitFor(() => expect(mocks.fetchDetailedReport).toHaveBeenCalledTimes(1));
    expect(mocks.fetchDetailedReport.mock.calls[0][0]).not.toHaveProperty("childTenantId");
    expect(mocks.fetchNetworkDetailedReport).not.toHaveBeenCalled();
    expect(screen.queryByLabelText(ETIQUETA_ALCANCE)).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Cliente" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Descargar Excel/ }));
    await waitFor(() => expect(mocks.exportDetailedReport).toHaveBeenCalledTimes(1));
    expect(mocks.exportNetworkDetailedReport).not.toHaveBeenCalled();
  });

  it("Reportes 2.0: sin selector y con los exports de siempre", async () => {
    sinJerarquia();
    render(<Reportes />);
    await waitFor(() => expect(mocks.fetchAnalyticsOverview).toHaveBeenCalled());
    expect(screen.queryByLabelText(ETIQUETA_ALCANCE)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Exportar Excel/ })).toBeInTheDocument();
    expect(mocks.fetchNetworkAnalyticsOverview).not.toHaveBeenCalled();
  });
});

// ── AC5 — un solo control de alcance y una sola preferencia ───────────────────

describe("HU #12364 AC5 — un solo control de alcance", () => {
  it("el Dashboard monta el mismo NetworkScopeSelector de trámites y lee/escribe la misma preferencia tramites.scope", async () => {
    cabeza({ mode: "own" });
    const user = userEvent.setup();
    render(<Dashboard onNewTramite={() => {}} />);

    // Misma preferencia por usuario en el servidor (no localStorage).
    await waitFor(() => expect(mocks.prefsGet).toHaveBeenCalledWith(SCOPE_KEY));
    const select = await screen.findByTestId("dashboard-network-scope-select");
    // Mismo control: el rótulo y las opciones son las del componente compartido.
    expect(screen.getByLabelText(ETIQUETA_ALCANCE)).toBe(select);
    await waitFor(() => expect(within(select).getAllByRole("option").length).toBe(4));

    await user.selectOptions(select, "network");
    expect(mocks.prefsPut).toHaveBeenCalledWith(SCOPE_KEY, { mode: "network" });
    await waitFor(() => expect(mocks.fetchNetworkAnalyticsOverview).toHaveBeenCalled());
    expect(screen.getByDisplayValue(ETIQUETA_ALCANCE_RED)).toBe(select);
  });

  it("un alcance elegido en trámites (preferencia guardada) se refleja en el reporte detallado sin volver a elegirlo", async () => {
    // Lo que el listado de trámites guardó en `tramites.scope` es lo que lee el reporte.
    cabeza({ mode: "network", childTenantId: HIJO_2 });
    render(<DetailedReportPanel />);
    await waitFor(() => expect(mocks.prefsGet).toHaveBeenCalledWith(SCOPE_KEY));
    await waitFor(() =>
      expect(mocks.fetchNetworkDetailedReport).toHaveBeenLastCalledWith(
        expect.objectContaining({ childTenantId: HIJO_2 }),
        expect.anything(),
      ),
    );
    const select = await screen.findByTestId("detallado-network-scope-select");
    await waitFor(() => expect((select as HTMLSelectElement).value).toBe(`child:${HIJO_2}`));
  });

  it("el componente compartido es literalmente el mismo módulo (sin segunda implementación)", () => {
    const onChange = vi.fn();
    render(<NetworkScopeSelector scope={{ mode: "own" }} onChange={onChange} testId="directo" />);
    const directo = screen.getByTestId("directo");
    expect(screen.getByLabelText(ETIQUETA_ALCANCE)).toBe(directo);
    expect(within(directo).getAllByRole("option").map((o) => (o as HTMLOptionElement).value)).toEqual(["own", "network"]);
  });
});

// ── AC6 — sin documentos ni anexos ────────────────────────────────────────────

describe("HU #12364 AC6 — sin documentos ni anexos", () => {
  it("con la red activa ni el Dashboard ni el reporte ofrecen descargar documentos o anexos de los hijos", async () => {
    cabeza({ mode: "network", childTenantId: HIJO });
    const { unmount } = render(<Dashboard onNewTramite={() => {}} />);
    await screen.findByTestId("kpi-red-Total Trámites");
    expect(screen.queryByRole("button", { name: /anexo|documento|paquete|descargar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /anexo|documento|paquete|descargar/i })).not.toBeInTheDocument();
    unmount();

    render(<DetailedReportPanel />);
    await screen.findByRole("columnheader", { name: "Cliente" });
    // El único «Descargar» es el Excel del reporte (AC3); nada por fila ni por trámite.
    const descargas = screen.getAllByRole("button", { name: /descargar/i });
    expect(descargas).toHaveLength(1);
    expect(descargas[0]).toHaveTextContent("Descargar Excel");
    expect(screen.queryByRole("button", { name: /anexo|documento|paquete/i })).not.toBeInTheDocument();
    expect(document.querySelector("a[download]")).toBeNull();
  });

  it("en Reportes 2.0 con la red activa el drill-down al detalle de trámites no se abre", async () => {
    cabeza({ mode: "network" });
    const user = userEvent.setup();
    render(<Reportes />);
    await waitFor(() => expect(mocks.fetchNetworkAnalyticsOverview).toHaveBeenCalled());
    // Los donuts siguen renderizados; pulsar un segmento no abre el panel (que usa rutas propias).
    const segmentos = await screen.findAllByRole("button", { name: /matr[ií]culas/i });
    await user.click(segmentos[0]);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /anexo|documento|paquete/i })).not.toBeInTheDocument();
  });
});

// ── HU #12652 — el alcance de red es exclusivo del AdminCompany de la cabeza ──

/**
 * HU #12652 — Alcance de red exclusivo del administrador de la cabeza.
 *
 * Uso de ejemplo: un Radicador de la cabeza (claim `is_group_parent`, sin AdminCompany) abre el
 * Dashboard, Reportes o el reporte detallado: no ve el selector «Alcance» ni ningún chip «Red», y
 * las consultas son las propias de siempre (nunca `network/**`), aunque tenga guardada una
 * preferencia `tramites.scope=network` de otra sesión. El AdminCompany (solo o multi-rol) sigue
 * viendo el selector con las hijas.
 */
function cabezaSinAdmin(scope: unknown = { mode: "network" }) {
  mocks.usePermissions.mockReturnValue(
    permisos({ isGroupParent: true, isAdminCompany: false, roleCode: "Radicador" }),
  );
  mocks.prefsGet.mockResolvedValue({ value: scope });
  mocks.fetchNetworkChildren.mockResolvedValue(HIJOS);
}

describe("HU #12652 — alcance de red exclusivo del AdminCompany de la cabeza", () => {
  it("AC3/AC4 — Dashboard: Radicador de cabeza con preferencia `network` guardada no ve selector ni chips «Red» y consulta solo lo propio", async () => {
    cabezaSinAdmin({ mode: "network", childTenantId: HIJO });
    render(<Dashboard onNewTramite={() => {}} />);

    await waitFor(() => expect(mocks.fetchAnalyticsOverview).toHaveBeenCalledTimes(1));
    expect(mocks.fetchMonthlyTrend).toHaveBeenCalledTimes(1);
    expect(mocks.fetchNetworkAnalyticsOverview).not.toHaveBeenCalled();
    expect(mocks.fetchNetworkMonthlyTrend).not.toHaveBeenCalled();
    expect(mocks.fetchNetworkChildren).not.toHaveBeenCalled();
    expect(mocks.prefsGet).not.toHaveBeenCalled();
    expect(screen.queryByTestId("dashboard-network-scope-select")).not.toBeInTheDocument();
    expect(screen.queryByLabelText(ETIQUETA_ALCANCE)).not.toBeInTheDocument();
    expect(screen.queryByTestId("kpi-red-Total Trámites")).not.toBeInTheDocument();
    expect(screen.queryAllByRole("status", { name: new RegExp(`^${ETIQUETA_DISTINTIVO_RED}:`) })).toHaveLength(0);
    expect(screen.queryByText(/No tienes acceso a las métricas de la red/)).not.toBeInTheDocument();
  });

  it("AC3 — Reporte detallado: Radicador de cabeza sin selector, sin chip «Red» y consulta propia", async () => {
    cabezaSinAdmin({ mode: "network" });
    render(<DetailedReportPanel />);

    await waitFor(() => expect(mocks.fetchDetailedReport).toHaveBeenCalledTimes(1));
    expect(mocks.fetchNetworkDetailedReport).not.toHaveBeenCalled();
    expect(mocks.fetchNetworkChildren).not.toHaveBeenCalled();
    expect(screen.queryByTestId("detallado-network-scope-select")).not.toBeInTheDocument();
    expect(screen.queryByTestId("detallado-red-badge")).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Cliente" })).not.toBeInTheDocument();
  });

  it("AC3 — Reportes 2.0: Radicador de cabeza sin selector, sin chip «Red» y con los exports de siempre", async () => {
    cabezaSinAdmin({ mode: "network" });
    render(<Reportes />);

    await waitFor(() => expect(mocks.fetchAnalyticsOverview).toHaveBeenCalled());
    expect(mocks.fetchNetworkAnalyticsOverview).not.toHaveBeenCalled();
    expect(mocks.fetchNetworkChildren).not.toHaveBeenCalled();
    expect(screen.queryByLabelText(ETIQUETA_ALCANCE)).not.toBeInTheDocument();
    expect(screen.queryByTestId("reportes-red-badge")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Exportar Excel/ })).toBeInTheDocument();
  });

  it("AC1/AC5 — AdminCompany de cabeza (incluso multi-rol con Radicador como primer rol) sigue viendo el selector con las hijas en el Dashboard", async () => {
    mocks.usePermissions.mockReturnValue(
      permisos({ isGroupParent: true, isAdminCompany: true, roleCode: "Radicador", roleId: "r-rad" }),
    );
    mocks.prefsGet.mockResolvedValue({ value: { mode: "own" } });
    mocks.fetchNetworkChildren.mockResolvedValue(HIJOS);
    render(<Dashboard onNewTramite={() => {}} />);

    const select = await screen.findByTestId("dashboard-network-scope-select");
    await waitFor(() => expect(within(select).getAllByRole("option").length).toBe(4));
    expect(mocks.fetchNetworkChildren).toHaveBeenCalledTimes(1);
  });
});
