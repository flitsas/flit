// Tests de HU #12253 (Feature #12249) — módulos activos del Dashboard.
// Cubre AC1-AC5: visibilidad condicional de la sección de Trámites y tarjetas
// "Próximamente" para Comparendos/Resoluciones, según los flags que expone
// GET /api/v1/analytics/active-modules.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";

import type { ActiveModulesResponse, AnalyticsOverviewResponse } from "@/lib/api/types";

// ── Mocks de la capa de datos y de identidad (sin red real) ─────────────────
const mocks = vi.hoisted(() => ({
  fetchAnalyticsOverview: vi.fn(),
  fetchMonthlyTrend: vi.fn(),
  fetchActiveModules: vi.fn(),
  fetchCompaniesIndex: vi.fn(),
  listTenantBiometricValidations: vi.fn(),
  getToken: vi.fn(),
  decodeJwtPayload: vi.fn(),
  isSuperAdmin: vi.fn(),
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

import { Dashboard } from "@/components/atom/modules/Dashboard";

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

function noop() {}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getToken.mockReturnValue("token");
  mocks.decodeJwtPayload.mockReturnValue({ display_name: "Ana", email: "ana@flit.io" });
  mocks.isSuperAdmin.mockReturnValue(false);
  mocks.fetchAnalyticsOverview.mockResolvedValue(FULL_OVERVIEW);
  mocks.fetchMonthlyTrend.mockResolvedValue(TREND);
  mocks.listTenantBiometricValidations.mockResolvedValue(BIOMETRIC_EMPTY);
  mocks.fetchActiveModules.mockResolvedValue(ALL_ENABLED);
});

describe("Dashboard — HU #12253 módulos activos por tenant", () => {
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

    // Sin flags apagados, no debe verse ninguna tarjeta "Próximamente".
    expect(screen.queryByText("Próximamente")).not.toBeInTheDocument();
  });

  it("AC2: ComparendosModuleEnabled=false muestra la tarjeta Próximamente con el estilo de las tarjetas KPI existentes", async () => {
    mocks.fetchActiveModules.mockResolvedValue({
      tramitesModuleEnabled: true,
      comparendosModuleEnabled: false,
      resolucionesModuleEnabled: true,
    });

    render(<Dashboard onNewTramite={noop} />);

    const label = await screen.findByText("Comparendos");
    expect(screen.getByText("Próximamente")).toBeInTheDocument();

    const card = label.closest("div.rounded-2xl");
    expect(card).not.toBeNull();
    expect(card?.className).toContain("bg-white");
    expect(card?.className).toContain("dark:bg-[#0B0F14]");
    expect(card?.className).toContain("border");

    // Resoluciones sigue habilitado: no debe verse su placeholder.
    expect(screen.queryByText("Resoluciones")).not.toBeInTheDocument();
  });

  it("AC3: ResolucionesModuleEnabled=false muestra su propia tarjeta Próximamente, independiente de Comparendos", async () => {
    mocks.fetchActiveModules.mockResolvedValue({
      tramitesModuleEnabled: true,
      comparendosModuleEnabled: true,
      resolucionesModuleEnabled: false,
    });

    render(<Dashboard onNewTramite={noop} />);

    expect(await screen.findByText("Resoluciones")).toBeInTheDocument();
    expect(screen.getByText("Próximamente")).toBeInTheDocument();
    expect(screen.queryByText("Comparendos")).not.toBeInTheDocument();
  });

  it("AC4: al recargar el Dashboard con el flag ya activo, el placeholder de Comparendos desaparece sin reemplazarse por contenido real", async () => {
    mocks.fetchActiveModules.mockResolvedValueOnce({
      tramitesModuleEnabled: true,
      comparendosModuleEnabled: false,
      resolucionesModuleEnabled: true,
    });
    const { unmount } = render(<Dashboard onNewTramite={noop} />);
    expect(await screen.findByText("Comparendos")).toBeInTheDocument();
    unmount();

    // Simula la "recarga" del Dashboard (nuevo montaje) tras activar el flag desde Admin.
    mocks.fetchActiveModules.mockResolvedValueOnce({
      tramitesModuleEnabled: true,
      comparendosModuleEnabled: true,
      resolucionesModuleEnabled: true,
    });
    render(<Dashboard onNewTramite={noop} />);

    await waitFor(() => expect(mocks.fetchActiveModules).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByText("Comparendos")).not.toBeInTheDocument());
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
