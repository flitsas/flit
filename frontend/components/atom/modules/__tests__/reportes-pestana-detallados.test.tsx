import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type { DetailedReportPage, NetworkDetailedReportPage } from "@/lib/api/detailed-report";

/**
 * HU #13581 (Feature #13579, Épica #13271) — Reportes Detallados como pestaña del módulo Reportes.
 *
 * Uso de ejemplo: un usuario con `reportes.detallados.read` abre Reportes, ve la pestaña «Reportes
 * Detallados» después de «Operación / Trámites» y, al elegirla, la dirección queda en
 * `?reportesTab=detallados` con los filtros, indicadores, grilla y exportación del antiguo módulo; la
 * barra de filtros común de Reportes se oculta mientras la pestaña está activa.
 */

const mocks = vi.hoisted(() => ({
  fetchAnalyticsOverview: vi.fn(),
  fetchMonthlyTrend: vi.fn(),
  fetchTopProducers: vi.fn(),
  fetchNetworkAnalyticsOverview: vi.fn(),
  fetchNetworkMonthlyTrend: vi.fn(),
  fetchNetworkTopProducers: vi.fn(),
  fetchDetailedReport: vi.fn(),
  exportDetailedReport: vi.fn(),
  fetchNetworkDetailedReport: vi.fn(),
  exportNetworkDetailedReport: vi.fn(),
  fetchAllCompanies: vi.fn(),
  fetchNetworkChildren: vi.fn(),
  listPublishedProcedureTypes: vi.fn(),
  listTransitOffices: vi.fn(),
  usePermissions: vi.fn(),
  prefsGet: vi.fn(),
  prefsPut: vi.fn(),
  fetchLiveOverview: vi.fn(),
  fetchFunnel: vi.fn(),
}));

vi.mock("@/lib/api/analytics", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/analytics")>()),
  fetchAnalyticsOverview: mocks.fetchAnalyticsOverview,
  fetchMonthlyTrend: mocks.fetchMonthlyTrend,
  fetchTopProducers: mocks.fetchTopProducers,
  fetchNetworkAnalyticsOverview: mocks.fetchNetworkAnalyticsOverview,
  fetchNetworkMonthlyTrend: mocks.fetchNetworkMonthlyTrend,
  fetchNetworkTopProducers: mocks.fetchNetworkTopProducers,
}));
vi.mock("@/lib/api/analytics-v2", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/analytics-v2")>()),
  fetchLiveOverview: mocks.fetchLiveOverview,
  fetchFunnel: mocks.fetchFunnel,
}));
vi.mock("@/lib/api/detailed-report", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/detailed-report")>()),
  fetchDetailedReport: mocks.fetchDetailedReport,
  exportDetailedReport: mocks.exportDetailedReport,
  fetchNetworkDetailedReport: mocks.fetchNetworkDetailedReport,
  exportNetworkDetailedReport: mocks.exportNetworkDetailedReport,
}));
vi.mock("@/lib/api/admin-companies", () => ({ fetchAllCompanies: mocks.fetchAllCompanies }));
vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    listPublishedProcedureTypes: mocks.listPublishedProcedureTypes,
    listTransitOffices: mocks.listTransitOffices,
  },
  fetchNetworkChildren: mocks.fetchNetworkChildren,
}));
vi.mock("@/lib/api/ui-preferences", () => ({
  uiPreferencesClient: { get: mocks.prefsGet, put: mocks.prefsPut },
}));
vi.mock("@/hooks/usePermissions", () => ({ usePermissions: mocks.usePermissions }));

import { Reportes, GESTOR_REPORTES_TAB_DEFS } from "@/components/atom/modules/Reportes";
import { OT_REPORTES_TABS } from "@/components/admin/transit-offices/OtReportsConsole";
import { ETIQUETA_ALCANCE } from "@/lib/tramites/network-scope";

const TENANT = "11111111-1111-1111-1111-111111111111";
const HIJO = "22222222-2222-2222-2222-222222222222";

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
  personFullName: "Persona de prueba",
  hasTransformation: false,
  transformationDetail: null,
  isLeasing: false,
  paymentType: "",
  transferType: "Normal",
};
const PAGINA: DetailedReportPage = {
  items: [FILA],
  totalCount: 1,
  page: 1,
  pageSize: 10,
  summary: { totalCount: 1, byStatus: [], byCategory: [], byProcedureType: [] },
};
const PAGINA_RED: NetworkDetailedReportPage = {
  ...PAGINA,
  items: [{ ...FILA, tenantId: HIJO, tenantName: "Concesionario Hijo SAS" }],
};

const READ = "reportes.detallados.read";
const EXPORT = "reportes.detallados.export";

function permisos(permissions: string[], over: Record<string, unknown> = {}) {
  mocks.usePermissions.mockReturnValue({
    permissions,
    isSuperAdmin: false,
    isAdminCompany: true,
    isOtAdmin: false,
    isGroupParent: false,
    hasParentTenant: false,
    parentTenantId: null,
    tenantId: TENANT,
    userId: "user-1",
    roleId: "r1",
    roleCode: "AdminCompany",
    ...over,
  });
}

async function abrirPestana() {
  const user = userEvent.setup();
  render(<Reportes />);
  await user.click(await screen.findByRole("tab", { name: "Reportes Detallados" }));
  return user;
}

beforeEach(() => {
  vi.clearAllMocks();
  window.history.replaceState({}, "", "/");
  mocks.fetchAnalyticsOverview.mockResolvedValue({ tenantId: TENANT, from: "2026-09-01", to: "2026-09-30", categories: [] });
  mocks.fetchMonthlyTrend.mockResolvedValue({ items: [] });
  mocks.fetchTopProducers.mockResolvedValue({ items: [] });
  mocks.fetchNetworkAnalyticsOverview.mockResolvedValue({ tenantId: TENANT, from: "", to: "", categories: [], scope: { tenantIds: [TENANT] } });
  mocks.fetchNetworkMonthlyTrend.mockResolvedValue({ items: [], scope: { tenantIds: [TENANT] } });
  mocks.fetchNetworkTopProducers.mockResolvedValue({ items: [], scope: { tenantIds: [TENANT] } });
  mocks.fetchDetailedReport.mockResolvedValue(PAGINA);
  mocks.fetchNetworkDetailedReport.mockResolvedValue(PAGINA_RED);
  mocks.exportDetailedReport.mockResolvedValue(undefined);
  mocks.exportNetworkDetailedReport.mockResolvedValue(undefined);
  mocks.fetchAllCompanies.mockResolvedValue([]);
  mocks.listPublishedProcedureTypes.mockResolvedValue([]);
  mocks.listTransitOffices.mockResolvedValue([]);
  mocks.prefsPut.mockResolvedValue(undefined);
  mocks.fetchLiveOverview.mockResolvedValue({ generatedAt: "", today: { creados: 0, byStatus: [], entregados: 0, aprobados: 0, rechazados: 0 }, stuckCount: 0, pendingIdentityValidations: 0, integrationsLastHour: { calls: 0, errors: 0, avgDurationMs: 0 }, lastActivityAt: null });
  mocks.fetchFunnel.mockResolvedValue({ steps: [] });
});

describe("HU #13581 AC1 — la pestaña aparece en la barra de Reportes", () => {
  it("se ubica justo después de «Operación / Trámites» en la barra de pestañas", async () => {
    permisos(["reportes.resumen.read", "reportes.operacion.read", READ, "reportes.uso.read"]);
    render(<Reportes />);

    const tabs = within(await screen.findByRole("tablist", { name: "Pestañas de reportes" })).getAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual([
      "Resumen general",
      "Operación / Trámites",
      "Reportes Detallados",
      "Uso del aplicativo",
    ]);
  });

  it("contrato: la definición usa el slug reportes.detallados.read y va después de operación", () => {
    const ids = GESTOR_REPORTES_TAB_DEFS.map((t) => t.id);
    expect(ids.indexOf("detallados")).toBe(ids.indexOf("operacion") + 1);
    expect(GESTOR_REPORTES_TAB_DEFS.find((t) => t.id === "detallados")).toEqual({
      id: "detallados",
      label: "Reportes Detallados",
      slug: READ,
    });
  });

  it("usa el mismo estado de pestaña que las demás (aria-selected al elegirla)", async () => {
    permisos(["reportes.resumen.read", READ]);
    await abrirPestana();
    expect(screen.getByRole("tab", { name: "Reportes Detallados" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Resumen general" })).toHaveAttribute("aria-selected", "false");
  });
});

describe("HU #13581 AC2 — paridad funcional dentro de la pestaña", () => {
  it("deja la dirección en ?reportesTab=detallados y muestra filtros, grilla y paginación del reporte detallado", async () => {
    permisos(["reportes.resumen.read", READ]);
    await abrirPestana();

    expect(new URL(window.location.href).searchParams.get("reportesTab")).toBe("detallados");
    await waitFor(() => expect(mocks.fetchDetailedReport).toHaveBeenCalled());
    expect(await screen.findByText("TR-1")).toBeInTheDocument();
    expect(screen.getByText(/Filas por página/i)).toBeInTheDocument();
  });

  it("oculta la barra de filtros común mientras la pestaña está activa y la recupera al volver", async () => {
    permisos(["reportes.resumen.read", READ]);
    render(<Reportes />);
    expect(await screen.findByLabelText("Comparar con")).toBeInTheDocument();

    const user = userEvent.setup();
    await user.click(screen.getByRole("tab", { name: "Reportes Detallados" }));
    expect(screen.queryByLabelText("Comparar con")).not.toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "Resumen general" }));
    expect(await screen.findByLabelText("Comparar con")).toBeInTheDocument();
  });

  it("abre directo en la pestaña cuando la dirección ya trae ?reportesTab=detallados", async () => {
    permisos(["reportes.resumen.read", READ]);
    window.history.replaceState({}, "", "/?m=reportes&reportesTab=detallados");
    render(<Reportes />);

    expect(await screen.findByText("TR-1")).toBeInTheDocument();
    expect(mocks.fetchAnalyticsOverview).not.toHaveBeenCalled();
  });
});

describe("HU #13581 AC3 — exportación según permiso", () => {
  it("con reportes.detallados.export descarga el Excel con los filtros aplicados", async () => {
    permisos([READ, EXPORT]);
    const user = await abrirPestana();
    await screen.findByText("TR-1");

    await user.click(screen.getByRole("button", { name: /Descargar Excel/ }));
    await waitFor(() => expect(mocks.exportDetailedReport).toHaveBeenCalledTimes(1));
    expect(mocks.exportDetailedReport.mock.calls[0][0]).toEqual(
      expect.objectContaining({ from: expect.any(String), to: expect.any(String) }),
    );
  });

  it("sin reportes.detallados.export no muestra el botón de descarga", async () => {
    permisos([READ]);
    await abrirPestana();
    await screen.findByText("TR-1");
    expect(screen.queryByRole("button", { name: /Descargar Excel/ })).not.toBeInTheDocument();
  });
});

describe("HU #13581 AC4 — alcance de red de clientes", () => {
  it("la cabeza de red consulta la ruta de red, sin el aviso «solo tu compañía» y con un único selector de alcance", async () => {
    permisos(["reportes.resumen.read", READ, EXPORT], { isGroupParent: true });
    mocks.prefsGet.mockResolvedValue({ value: { mode: "network", childTenantId: HIJO } });
    mocks.fetchNetworkChildren.mockResolvedValue([{ id: HIJO, nombre: "Concesionario Hijo SAS" }]);
    const user = await abrirPestana();

    await waitFor(() => expect(mocks.fetchNetworkDetailedReport).toHaveBeenCalled());
    expect(mocks.fetchNetworkDetailedReport.mock.calls.at(-1)?.[0]).toEqual(expect.objectContaining({ childTenantId: HIJO }));
    expect(screen.queryByTestId("reportes-tab-no-disponible-red")).not.toBeInTheDocument();
    expect(screen.getAllByLabelText(ETIQUETA_ALCANCE)).toHaveLength(1);

    await user.click(screen.getByRole("button", { name: /Descargar Excel/ }));
    await waitFor(() => expect(mocks.exportNetworkDetailedReport).toHaveBeenCalledTimes(1));
    expect(mocks.exportDetailedReport).not.toHaveBeenCalled();
  });
});

describe("HU #13581 AC5 — sin permiso la pestaña no existe (negativo)", () => {
  it("con ?reportesTab=detallados y sin reportes.detallados.read abre la primera pestaña visible", async () => {
    permisos(["reportes.resumen.read", "reportes.operacion.read"]);
    window.history.replaceState({}, "", "/?m=reportes&reportesTab=detallados");
    render(<Reportes />);

    expect(await screen.findByRole("tab", { name: "Resumen general" })).toHaveAttribute("aria-selected", "true");
    expect(screen.queryByRole("tab", { name: "Reportes Detallados" })).not.toBeInTheDocument();
    expect(mocks.fetchDetailedReport).not.toHaveBeenCalled();
  });

  it("si es su única pestaña de reportes, la ve igual (nadie pierde acceso)", async () => {
    permisos([READ]);
    render(<Reportes />);

    const tabs = await screen.findAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual(["Reportes Detallados"]);
    expect(await screen.findByText("TR-1")).toBeInTheDocument();
    expect(screen.queryByTestId("reportes-sin-permisos")).not.toBeInTheDocument();
  });
});

describe("HU #13581 AC6 — usuario de Organismo de Tránsito (fuera de alcance)", () => {
  it("las pestañas del reporte del organismo no incluyen «Reportes Detallados»", () => {
    expect(OT_REPORTES_TABS.map((t) => t.label)).not.toContain("Reportes Detallados");
  });
});

describe("HU #13581 AC7 — estados de la pestaña", () => {
  it("cargando: muestra el estado de carga mientras la consulta está en curso", async () => {
    permisos([READ]);
    mocks.fetchDetailedReport.mockReturnValue(new Promise(() => {}));
    render(<Reportes />);
    expect(await screen.findByTestId("ui-loading")).toBeInTheDocument();
  });

  it("vacío: sin filas muestra el mensaje de rango sin trámites", async () => {
    permisos([READ]);
    mocks.fetchDetailedReport.mockResolvedValue({ ...PAGINA, items: [], totalCount: 0 });
    render(<Reportes />);
    expect(await screen.findByTestId("ui-empty")).toHaveTextContent("No hay trámites en el rango de fechas seleccionado.");
  });

  it("error: muestra el error y reintenta con el botón", async () => {
    permisos([READ]);
    mocks.fetchDetailedReport.mockRejectedValueOnce(new Error("boom")).mockResolvedValue(PAGINA);
    const user = userEvent.setup();
    render(<Reportes />);

    expect(await screen.findByTestId("ui-error")).toHaveTextContent("No se pudo cargar el reporte detallado.");
    await user.click(screen.getByRole("button", { name: /Reintentar/ }));
    expect(await screen.findByText("TR-1")).toBeInTheDocument();
  });

  it("lleno: pinta las filas de la consulta", async () => {
    permisos([READ]);
    render(<Reportes />);
    expect(await screen.findByText("TR-1")).toBeInTheDocument();
  });
});
