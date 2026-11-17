// Inicio del SuperAdmin (rediseño de los 3 dashboards, 2026-11-17). Cubre las 3 franjas: saludo y KPIs, tarjetas por
// organismo que filtran la distribución, y la tabla de compañías que fija la compañía de toda la vista (ratón, Enter y
// Espacio). Sin red real: se sustituye la capa de datos.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type { AnalyticsOverviewResponse } from "@/lib/api/types";
import type { SuperAdminCompaniasResponse, SuperAdminOrganismosResponse } from "@/lib/api/analytics-superadmin";

const FAMILIAS_READ = ["matriculas.read", "traspasos.read", "otros-tramites.read"];
const TENANT_A = "aaaaaaaa-0000-4000-8000-000000000001";
const TENANT_B = "bbbbbbbb-0000-4000-8000-000000000002";
const OT_BOG = "0f000000-0000-4000-8000-000000000001";
const OT_MED = "0f000000-0000-4000-8000-000000000002";

const mocks = vi.hoisted(() => ({
  fetchAnalyticsOverview: vi.fn(),
  fetchMonthlyTrend: vi.fn(),
  fetchAllCompanies: vi.fn(),
  fetchSuperAdminCompanias: vi.fn(),
  fetchSuperAdminOrganismos: vi.fn(),
  listTenantBiometricValidations: vi.fn(),
  getToken: vi.fn(),
  decodeJwtPayload: vi.fn(),
  getActiveBanners: vi.fn(),
}));

vi.mock("@/lib/api/analytics", () => ({
  fetchAnalyticsOverview: mocks.fetchAnalyticsOverview,
  fetchMonthlyTrend: mocks.fetchMonthlyTrend,
}));
vi.mock("@/lib/api/analytics-superadmin", () => ({
  fetchSuperAdminCompanias: mocks.fetchSuperAdminCompanias,
  fetchSuperAdminOrganismos: mocks.fetchSuperAdminOrganismos,
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
}));
vi.mock("@/lib/api/public-banners", () => ({
  getActiveBanners: mocks.getActiveBanners,
  bannerImageUrl: (id: string) => `http://api.test/api/v1/public/banners/${id}/image`,
}));

import { SuperAdminDashboard, distribucionDesdeOverview, formatoDias } from "@/components/atom/modules/SuperAdminDashboard";

const OVERVIEW: AnalyticsOverviewResponse = {
  tenantId: "00000000-0000-0000-0000-000000000000",
  from: null,
  to: null,
  categories: [
    {
      category: "matriculas",
      total: 10,
      byStatus: [
        { status: "aprobado", count: 6 },
        { status: "rechazado", count: 2 },
        { status: "entregado", count: 1 },
        { status: "anulado", count: 1 },
      ],
    },
    { category: "traspasos", total: 2, byStatus: [{ status: "borrador", count: 2 }] },
    { category: "otros", total: 0, byStatus: [] },
  ],
};

const ORGANISMOS: SuperAdminOrganismosResponse = {
  tenantId: null,
  from: null,
  to: null,
  decididos: 8,
  tiempoPromedioDias: 2.5,
  items: [
    {
      transitOfficeId: OT_BOG,
      nombre: "Tránsito Bogotá",
      volumen: 9,
      decididos: 8,
      tiempoPromedioDias: 2.5,
      distribucion: [{ categoria: "matriculas", completados: 6, enProceso: 1, rechazados: 2 }],
    },
    {
      transitOfficeId: OT_MED,
      nombre: "Tránsito Medellín",
      volumen: 3,
      decididos: 0,
      tiempoPromedioDias: null,
      distribucion: [{ categoria: "traspasos", completados: 0, enProceso: 3, rechazados: 0 }],
    },
  ],
};

const COMPANIAS: SuperAdminCompaniasResponse = {
  from: null,
  to: null,
  stuckDays: 7,
  items: [
    { tenantId: TENANT_A, nombre: "Renting Uno", total: 8, activos: 5, aprobados: 3, rechazados: 1, tasaAprobacionPct: 75, represados: 2 },
    { tenantId: TENANT_B, nombre: "Concesionario Dos", total: 0, activos: 0, aprobados: 0, rechazados: 0, tasaAprobacionPct: null, represados: 0 },
  ],
};

function renderVista() {
  return render(<SuperAdminDashboard onNewTramite={() => {}} />);
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getToken.mockReturnValue("token");
  mocks.decodeJwtPayload.mockReturnValue({ display_name: "Sofía", role: "SuperAdmin", permissions: FAMILIAS_READ });
  mocks.getActiveBanners.mockResolvedValue([]);
  mocks.fetchAllCompanies.mockResolvedValue([
    { id: TENANT_A, razonSocial: "Renting Uno", nit: "900", code: "COMP-1", tenantType: "RENTING", isTransitOffice: false },
    { id: TENANT_B, razonSocial: "Concesionario Dos", nit: "901", code: "COMP-2", tenantType: "CONCESIONARIO", isTransitOffice: false },
  ]);
  mocks.fetchAnalyticsOverview.mockResolvedValue(OVERVIEW);
  mocks.fetchSuperAdminOrganismos.mockResolvedValue(ORGANISMOS);
  mocks.fetchSuperAdminCompanias.mockResolvedValue(COMPANIAS);
  mocks.listTenantBiometricValidations.mockResolvedValue({
    validations: [],
    stats: { total: 14, aprobadas: 10, enProceso: 2, rechazadas: 2, expiradas: 0 },
    total: 14,
  });
  mocks.fetchMonthlyTrend.mockResolvedValue({
    items: [
      { year: 2026, month: 10, category: "matriculas", total: 3 },
      { year: 2026, month: 11, category: "matriculas", total: 4 },
    ],
  });
});

describe("SuperAdminDashboard — franja 1", () => {
  it("saluda con «Visión del ecosistema» y el total del periodo de todas las compañías", async () => {
    renderVista();
    expect(await screen.findByText("Visión del ecosistema")).toBeInTheDocument();
    expect(screen.getByText("Hola, Sofía")).toBeInTheDocument();
    // Suma de companias-resumen (8 + 0), no el overview global (12, que incluiría tenants OT).
    expect(await screen.findByText("8 trámites")).toBeInTheDocument();
    expect(screen.getByTestId("dashboard-hero-banner")).toHaveTextContent("Todas las compañías · 8 trámites en el periodo seleccionado.");
  });

  it("arranca SIN rango (BUG #12588) y pide la vista global de validaciones", async () => {
    renderVista();
    await waitFor(() => expect(mocks.fetchAnalyticsOverview).toHaveBeenCalled());
    expect(mocks.fetchAnalyticsOverview.mock.calls[0][0]).toMatchObject({ from: "", to: "", tenantId: undefined });
    expect(mocks.fetchSuperAdminOrganismos.mock.calls[0][0]).toMatchObject({ tenantId: undefined });
    expect(mocks.fetchSuperAdminCompanias.mock.calls[0][0]).toMatchObject({ stuckDays: 7 });
    expect(mocks.listTenantBiometricValidations).toHaveBeenCalledWith(
      expect.objectContaining({ createdFrom: undefined, createdTo: undefined }),
      "*",
    );
  });

  it("pinta los 3 KPIs: total con % de aprobación, tiempo en días y validaciones", async () => {
    renderVista();
    const total = await screen.findByTestId("kpi-tramites-totales");
    await waitFor(() => expect(total).toHaveTextContent("8"));
    expect(total).not.toHaveTextContent("12");
    // Suma de la tabla: 3 aprobados / (3 + 1 rechazado) = 75 %.
    expect(total).toHaveTextContent(/75\s%\saprobación/);
    await waitFor(() => expect(screen.getByTestId("kpi-tiempo-ecosistema")).toHaveTextContent("2,5 días"));
    expect(screen.getByTestId("kpi-tiempo-ecosistema")).toHaveTextContent("hasta que el organismo aprueba o rechaza");
    await waitFor(() => expect(screen.getByTestId("kpi-validaciones")).toHaveTextContent("14"));
  });

  it("el total por compañía sale de su fila: con Concesionario Dos (0 trámites) el KPI baja a 0", async () => {
    mocks.fetchSuperAdminCompanias.mockResolvedValue({
      ...COMPANIAS,
      items: [COMPANIAS.items[0], { ...COMPANIAS.items[1], total: 3, activos: 3 }],
    });
    const user = userEvent.setup();
    renderVista();
    await user.click(await screen.findByRole("button", { name: "Ver resumen de Concesionario Dos" }));
    await waitFor(() => expect(screen.getByTestId("kpi-tramites-totales")).toHaveTextContent(/^Trámites totales3/));
    expect(screen.getByTestId("kpi-tramites-totales")).toHaveTextContent("Sin trámites decididos");
  });

  it("un KPI vacío se dice en palabras, sin guion", async () => {
    mocks.fetchSuperAdminOrganismos.mockResolvedValue({ ...ORGANISMOS, decididos: 0, tiempoPromedioDias: null, items: [] });
    mocks.fetchSuperAdminCompanias.mockResolvedValue({
      ...COMPANIAS,
      items: COMPANIAS.items.map((c) => ({ ...c, aprobados: 0, rechazados: 0, tasaAprobacionPct: null })),
    });
    renderVista();
    await waitFor(() => expect(screen.getByTestId("kpi-tiempo-ecosistema")).toHaveTextContent("Sin decisiones"));
    expect(screen.getByTestId("kpi-tramites-totales")).toHaveTextContent("Sin trámites decididos");
    expect(screen.getByTestId("kpi-tramites-totales")).not.toHaveTextContent("—");
  });

  it("ya no pinta biometría en cajitas, seguimiento de 6 meses ni «Próximamente»", async () => {
    renderVista();
    await screen.findByTestId("organismos-grid");
    expect(screen.queryByText("Validaciones Biométricas")).not.toBeInTheDocument();
    expect(screen.queryByText("Seguimiento operativo")).not.toBeInTheDocument();
    expect(screen.queryByText("Próximamente")).not.toBeInTheDocument();
  });
});

describe("SuperAdminDashboard — franja 2: organismos", () => {
  it("una tarjeta por organismo con volumen y tiempo en días; sin decisiones se dice en palabras", async () => {
    renderVista();
    const bog = await screen.findByTestId(`organismo-${OT_BOG}`);
    expect(bog).toHaveTextContent("Tránsito Bogotá");
    expect(bog).toHaveTextContent("9");
    expect(bog).toHaveTextContent("2,5 días de promedio");
    expect(screen.getByTestId(`organismo-${OT_MED}`)).toHaveTextContent("Sin decisiones en el periodo");
    expect(screen.queryByText(/SLA|meta/i)).not.toBeInTheDocument();
  });

  it("pulsar un organismo filtra «Distribución de operaciones» y volver a pulsarlo lo quita", async () => {
    const user = userEvent.setup();
    renderVista();
    const panel = await screen.findByTestId("panel-operaciones");
    expect(within(panel).getByText("Todos los organismos")).toBeInTheDocument();

    const med = await screen.findByTestId(`organismo-${OT_MED}`);
    await user.click(med);
    expect(med).toHaveAttribute("aria-pressed", "true");
    expect(within(panel).getByText("Tránsito Medellín")).toBeInTheDocument();
    // Lectura accesible de la gráfica: solo los traspasos de Medellín.
    expect(within(panel).getByText(/Traspasos: Completados 0, En proceso 3, Rechazados 0/)).toBeInTheDocument();
    expect(within(panel).getByText(/Matrículas: Completados 0, En proceso 0, Rechazados 0/)).toBeInTheDocument();

    await user.click(med);
    expect(med).toHaveAttribute("aria-pressed", "false");
    expect(within(panel).getByText("Todos los organismos")).toBeInTheDocument();
    expect(within(panel).getByText(/Matrículas: Completados 6, En proceso 1, Rechazados 2/)).toBeInTheDocument();
  });
});

describe("SuperAdminDashboard — franja 3: compañías", () => {
  it("la tabla lista compañía, activos, tasa y represados; sin decididos lo dice en palabras", async () => {
    mocks.fetchSuperAdminCompanias.mockResolvedValue({
      ...COMPANIAS,
      items: [...COMPANIAS.items.slice(0, 1), { ...COMPANIAS.items[1], total: 1, activos: 1 }],
    });
    renderVista();
    const panel = await screen.findByTestId("panel-companias");
    expect(await within(panel).findByRole("button", { name: "Ver resumen de Renting Uno" })).toBeInTheDocument();
    expect(within(panel).getByText(/^75\s%$/)).toBeInTheDocument();
    expect(within(panel).getByText("Sin decididos")).toBeInTheDocument();
  });

  it("muestra solo las 5 compañías con más trámites activos y cuenta el resto en palabras", async () => {
    const muchas = Array.from({ length: 7 }, (_, i) => ({
      ...COMPANIAS.items[0],
      tenantId: `cccccccc-0000-4000-8000-${String(i).padStart(12, "0")}`,
      nombre: `Compañía ${i + 1}`,
    }));
    mocks.fetchSuperAdminCompanias.mockResolvedValue({ ...COMPANIAS, items: [...muchas, COMPANIAS.items[1]] });
    renderVista();
    const panel = await screen.findByTestId("panel-companias");
    expect(await within(panel).findByRole("button", { name: "Ver resumen de Compañía 5" })).toBeInTheDocument();
    expect(within(panel).queryByRole("button", { name: "Ver resumen de Compañía 6" })).not.toBeInTheDocument();
    expect(within(panel).queryByText(/Filas por página/i)).not.toBeInTheDocument();
    expect(screen.getByTestId("companias-otras")).toHaveTextContent("2 compañías más tienen trámites en el periodo");
    expect(screen.getByTestId("companias-sin-tramites")).toHaveTextContent("1 compañía activa no tiene trámites en el periodo seleccionado.");
  });

  it.each([
    ["Enter", "{Enter}"],
    ["Espacio", " "],
  ])("con %s sobre una compañía la fija en toda la vista", async (_nombre, tecla) => {
    const user = userEvent.setup();
    renderVista();
    const boton = await screen.findByRole("button", { name: "Ver resumen de Renting Uno" });
    boton.focus();
    await user.keyboard(tecla);

    const resumen = await screen.findByTestId("panel-resumen-compania");
    expect(within(resumen).getByText("Resumen de compañía")).toBeInTheDocument();
    await waitFor(() =>
      expect(mocks.fetchAnalyticsOverview).toHaveBeenLastCalledWith(expect.objectContaining({ tenantId: TENANT_A }), expect.anything()),
    );
    expect(mocks.fetchSuperAdminOrganismos).toHaveBeenLastCalledWith(expect.objectContaining({ tenantId: TENANT_A }), expect.anything());
    expect(mocks.listTenantBiometricValidations).toHaveBeenLastCalledWith(expect.anything(), TENANT_A);
  });

  it("con compañía elegida muestra las 3 mini tarjetas y «← Ver todas» vuelve a la tabla", async () => {
    const user = userEvent.setup();
    renderVista();
    await user.click(await screen.findByRole("button", { name: "Ver resumen de Renting Uno" }));

    const resumen = await screen.findByTestId("panel-resumen-compania");
    expect(within(resumen).getByText("Trámites activos")).toBeInTheDocument();
    expect(within(resumen).getByText("5")).toBeInTheDocument();
    expect(within(resumen).getByText(/^75\s%$/)).toBeInTheDocument();
    expect(within(resumen).getByText("Alertas / Represados")).toBeInTheDocument();
    expect(within(resumen).getByText("2")).toBeInTheDocument();
    expect(screen.getByTestId("kpi-tramites-totales")).toHaveTextContent("8");
    await waitFor(() => expect(mocks.fetchMonthlyTrend).toHaveBeenCalledWith(expect.objectContaining({ tenantId: TENANT_A }), expect.anything()));
    expect(screen.getByTestId("dashboard-hero-banner")).toHaveTextContent("Renting Uno ·");

    await user.click(within(resumen).getByRole("button", { name: "← Ver todas" }));
    expect(await screen.findByTestId("panel-companias")).toBeInTheDocument();
  });

  it("un error del resumen de compañías no tumba las otras franjas", async () => {
    mocks.fetchSuperAdminCompanias.mockRejectedValue(new Error("caído"));
    renderVista();
    expect(await screen.findByText("No se pudo cargar el resumen de compañías.")).toBeInTheDocument();
    expect(await screen.findByTestId(`organismo-${OT_BOG}`)).toBeInTheDocument();
  });
});

describe("SuperAdminDashboard — utilidades", () => {
  it("distribucionDesdeOverview reparte aprobado / rechazado / resto no final", () => {
    expect(distribucionDesdeOverview(OVERVIEW)[0]).toEqual({ categoria: "matriculas", completados: 6, enProceso: 1, rechazados: 2 });
  });

  it("formatoDias usa singular, coma decimal y un decimal", () => {
    expect(formatoDias(1)).toBe("1 día");
    expect(formatoDias(2.46)).toBe("2,5 días");
  });
});
