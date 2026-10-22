// HU #13665 (Feature #13636 F-F, Épica #12753, criterio FF-4) — sin Ver de una familia, su tarjeta KPI no aparece en el
// inicio (Dashboard) ni en Reportes › Resumen general, ni su serie en los gráficos por familia. La API (HU #13664) ya no
// la cuenta; la UI no la pinta en cero. En modo compatibilidad (token sin permisos por familia) no se oculta nada.
//
// Uso de ejemplo: token con `matriculas.read` y `otros-tramites.read` (sin `traspasos.read`) → sin tarjeta «Traspasos».
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";

import type { AnalyticsOverviewResponse } from "@/lib/api/types";

const mocks = vi.hoisted(() => ({
  fetchAnalyticsOverview: vi.fn(),
  fetchMonthlyTrend: vi.fn(),
  fetchActiveModules: vi.fn(),
  fetchNetworkAnalyticsOverview: vi.fn(),
  fetchNetworkMonthlyTrend: vi.fn(),
  fetchAllCompanies: vi.fn(),
  listTenantBiometricValidations: vi.fn(),
  getToken: vi.fn(),
  decodeJwtPayload: vi.fn(),
  isSuperAdmin: vi.fn(),
  getActiveBanners: vi.fn(),
}));

vi.mock("@/lib/api/analytics", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/analytics")>()),
  fetchAnalyticsOverview: mocks.fetchAnalyticsOverview,
  fetchMonthlyTrend: mocks.fetchMonthlyTrend,
  fetchActiveModules: mocks.fetchActiveModules,
  fetchNetworkAnalyticsOverview: mocks.fetchNetworkAnalyticsOverview,
  fetchNetworkMonthlyTrend: mocks.fetchNetworkMonthlyTrend,
}));
vi.mock("@/lib/api/admin-companies", () => ({ fetchAllCompanies: mocks.fetchAllCompanies }));
vi.mock("@/lib/api/tramites-client", () => ({
  ALL_TENANTS: "*",
  tramitesClient: { listTenantBiometricValidations: mocks.listTenantBiometricValidations },
}));
vi.mock("@/lib/api/client", () => ({ getToken: mocks.getToken }));
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
import { ResumenTab } from "@/components/atom/modules/_reportes/tabs/ResumenTab";

// Lo que devuelve la API a quien no tiene Ver de Traspasos: la categoría ya no viene.
const OVERVIEW_SIN_TRASPASOS: AnalyticsOverviewResponse = {
  tenantId: "11111111-1111-1111-1111-111111111111",
  from: null,
  to: null,
  categories: [
    { category: "matriculas", total: 5, byStatus: [{ status: "aprobado", count: 5 }] },
    { category: "otros", total: 1, byStatus: [{ status: "entregado", count: 1 }] },
  ],
} as unknown as AnalyticsOverviewResponse;

const SIN_TRASPASOS = ["dashboard.read", "reportes.read", "matriculas.read", "otros-tramites.read"];
const COMPATIBILIDAD = ["dashboard.read", "reportes.read"];

function sesion(permissions: string[]) {
  mocks.getToken.mockReturnValue("token");
  mocks.decodeJwtPayload.mockReturnValue({ display_name: "Ana", email: "ana@flit.io", permissions });
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.isSuperAdmin.mockReturnValue(false);
  mocks.fetchAnalyticsOverview.mockResolvedValue(OVERVIEW_SIN_TRASPASOS);
  mocks.fetchMonthlyTrend.mockResolvedValue({ items: [] });
  mocks.fetchActiveModules.mockResolvedValue({
    tramitesModuleEnabled: true,
    comparendosModuleEnabled: false,
    resolucionesModuleEnabled: false,
  });
  mocks.fetchAllCompanies.mockResolvedValue([]);
  mocks.listTenantBiometricValidations.mockResolvedValue({
    validations: [],
    stats: { total: 0, aprobadas: 0, enProceso: 0, rechazadas: 0, expiradas: 0 },
    page: 1,
    pageSize: 10,
    total: 0,
  });
  mocks.getActiveBanners.mockResolvedValue([]);
});

describe("Inicio (Dashboard) — tarjetas KPI por familia", () => {
  it("sin Ver Traspasos, la tarjeta «Traspasos» no aparece y las demás sí", async () => {
    sesion(SIN_TRASPASOS);
    render(<Dashboard onNewTramite={() => {}} />);

    const grid = await screen.findByTestId("dashboard-kpi-grid");
    expect(within(grid).queryByText("Traspasos")).not.toBeInTheDocument();
    expect(within(grid).getByText("Matrículas")).toBeInTheDocument();
    expect(within(grid).getByText("Otros Trámites")).toBeInTheDocument();
    expect(within(grid).getByText("Completados")).toBeInTheDocument();
    // Leyenda de «Seguimiento operativo»: tampoco la serie de Traspasos.
    expect(screen.queryByText("Traspasos")).not.toBeInTheDocument();
  });

  it("token sin permisos por familia (compatibilidad): no se oculta ninguna tarjeta", async () => {
    sesion(COMPATIBILIDAD);
    render(<Dashboard onNewTramite={() => {}} />);

    const grid = await screen.findByTestId("dashboard-kpi-grid");
    expect(within(grid).getByText("Traspasos")).toBeInTheDocument();
    expect(within(grid).getByText("Matrículas")).toBeInTheDocument();
  });
});

describe("Reportes › Resumen general — KPI, donut y serie por familia", () => {
  const filtros = {
    range: { from: "2026-10-01", to: "2026-10-31" },
    tenantId: "",
    transitOfficeId: "",
    procedureTypeId: "",
    operatorUserId: "",
    compareWith: "" as const,
  };

  it("sin Ver Traspasos, no hay KPI ni donut de Traspasos", async () => {
    sesion(SIN_TRASPASOS);
    render(<ResumenTab filters={filtros} needsCompany onDrillDown={() => {}} />);

    expect(await screen.findAllByText("Matrículas")).not.toHaveLength(0);
    expect(screen.getAllByText("Otros")).not.toHaveLength(0);
    expect(screen.queryByText("Traspasos")).not.toBeInTheDocument();
  });

  it("con las tres familias, la tarjeta de Traspasos sigue (en cero si no hay datos)", async () => {
    sesion([...SIN_TRASPASOS, "traspasos.read"]);
    render(<ResumenTab filters={filtros} needsCompany onDrillDown={() => {}} />);

    expect(await screen.findAllByText("Traspasos")).not.toHaveLength(0);
  });
});
