// Inicio del Gestor (rediseño de los 3 dashboards): KPIs por familia, franja de estados que filtra la cola de gestión,
// KPI sin dato dicho en palabras y gate de familia.
//
// Uso de ejemplo: pulsar «Preparado» en «Estado general de trámites» → la cola se vuelve a pedir con estado=preparado.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";

import type { AnalyticsOverviewResponse } from "@/lib/api/types";

const mocks = vi.hoisted(() => ({
  fetchAnalyticsOverview: vi.fn(),
  fetchNetworkAnalyticsOverview: vi.fn(),
  fetchColaInactividad: vi.fn(),
  searchEstadoCounts: vi.fn(),
  searchInstances: vi.fn(),
  getToken: vi.fn(),
  decodeJwtPayload: vi.fn(),
  getActiveBanners: vi.fn(),
}));

vi.mock("@/lib/api/analytics", () => ({
  fetchAnalyticsOverview: mocks.fetchAnalyticsOverview,
  fetchNetworkAnalyticsOverview: mocks.fetchNetworkAnalyticsOverview,
}));
vi.mock("@/lib/api/analytics-gestor", () => ({
  fetchColaInactividad: mocks.fetchColaInactividad,
}));
vi.mock("@/lib/api/tramites-client", () => {
  class TramitesApiError extends Error {
    constructor(
      public readonly status: number,
      message: string,
    ) {
      super(message);
    }
  }
  return {
    TramitesApiError,
    tramitesClient: { searchEstadoCounts: mocks.searchEstadoCounts, searchInstances: mocks.searchInstances },
  };
});
vi.mock("@/lib/api/client", () => ({ getToken: mocks.getToken }));
vi.mock("@/lib/auth/jwt", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/auth/jwt")>()),
  decodeJwtPayload: mocks.decodeJwtPayload,
}));
vi.mock("@/lib/api/public-banners", () => ({
  getActiveBanners: mocks.getActiveBanners,
  bannerImageUrl: (id: string) => `http://api.test/api/v1/public/banners/${id}/image`,
}));
// Quien no es cabeza de red: el hook no llama a nada y el alcance es el propio.
vi.mock("@/hooks/useNetworkScope", () => ({
  useNetworkScope: () => ({
    isGroupParent: false,
    networkActive: false,
    ready: true,
    scope: { mode: "propio", childTenantId: undefined },
    setScope: vi.fn(),
    children: [],
    childrenStatus: "idle",
    saving: false,
  }),
}));

import { GestorDashboard } from "@/components/atom/modules/GestorDashboard";

const OVERVIEW: AnalyticsOverviewResponse = {
  tenantId: "11111111-1111-1111-1111-111111111111",
  from: null,
  to: null,
  categories: [
    { category: "matriculas", total: 12, byStatus: [{ status: "preparado", count: 12 }] },
    { category: "traspasos", total: 7, byStatus: [{ status: "borrador", count: 7 }] },
    { category: "otros", total: 0, byStatus: [] },
  ],
} as unknown as AnalyticsOverviewResponse;

const TODAS = ["dashboard.read", "matriculas.read", "traspasos.read", "otros-tramites.read"];

function sesion(permissions: string[]) {
  mocks.getToken.mockReturnValue("token");
  mocks.decodeJwtPayload.mockReturnValue({ display_name: "Carolina Mendoza", permissions });
}

const fila = (dias: number, referencia: string) => ({
  id: `id-${referencia}`,
  referencia,
  placa: "ABC123",
  vin: null,
  estado: "preparado",
  familia: "matriculas",
  diasSinMovimiento: dias,
});

beforeEach(() => {
  vi.clearAllMocks();
  sesion(TODAS);
  mocks.fetchAnalyticsOverview.mockResolvedValue(OVERVIEW);
  // 8 borradores, 5 de ellos en el atajo «Trámites sin firmas» → 3 con firmas.
  mocks.searchInstances.mockResolvedValue({ items: [], total: 5 });
  mocks.fetchColaInactividad.mockResolvedValue({ items: [fila(14, "FT1-0000001"), fila(4, "FT1-0000002")], total: 9 });
  mocks.searchEstadoCounts.mockResolvedValue({ borrador: 8, preparado: 8, subsanacion: 3, aprobado: 1 });
  mocks.getActiveBanners.mockResolvedValue([]);
});

describe("Inicio del Gestor — franja 1", () => {
  it("saluda con datos reales: listos para radicar y en subsanación", async () => {
    render(<GestorDashboard onNewTramite={() => {}} />);

    expect(await screen.findByText("Hola, Carolina Mendoza")).toBeInTheDocument();
    expect(screen.getByText("Tu operación, sin pausas")).toBeInTheDocument();
    expect(await screen.findByText(/8 trámites listos para radicar/)).toBeInTheDocument();
    expect(screen.getByText(/3 en subsanación/)).toBeInTheDocument();
  });

  it("pinta los 3 KPIs por familia y arranca sin rango (BUG #12588)", async () => {
    render(<GestorDashboard onNewTramite={() => {}} />);

    const grid = await screen.findByTestId("dashboard-kpi-grid");
    expect(await within(grid).findByText("12")).toBeInTheDocument();
    expect(within(grid).getByText("Matrícula inicial")).toBeInTheDocument();
    expect(within(grid).getByText("7")).toBeInTheDocument();
    expect(within(grid).getByText("Otros trámites")).toBeInTheDocument();
    expect(mocks.fetchAnalyticsOverview).toHaveBeenCalledWith({ from: "", to: "" }, expect.any(AbortSignal));
  });

  it("un KPI vacío se dice en palabras, no con un guion", async () => {
    render(<GestorDashboard onNewTramite={() => {}} />);

    const otros = await screen.findByTestId("kpi-otros");
    expect(await within(otros).findByText("Ninguno")).toBeInTheDocument();
    expect(within(otros).queryByText("—")).not.toBeInTheDocument();
  });

  it("si el overview falla, el KPI dice «Sin datos»", async () => {
    mocks.fetchAnalyticsOverview.mockRejectedValue(new Error("caída"));
    render(<GestorDashboard onNewTramite={() => {}} />);

    const grid = await screen.findByTestId("dashboard-kpi-grid");
    expect(await within(grid).findAllByText("Sin datos")).toHaveLength(3);
    expect(within(grid).queryByText("—")).not.toBeInTheDocument();
  });

  it("sin Ver Traspasos, no hay tarjeta ni opción de Traspasos", async () => {
    sesion(["dashboard.read", "matriculas.read", "otros-tramites.read"]);
    render(<GestorDashboard onNewTramite={() => {}} />);

    const grid = await screen.findByTestId("dashboard-kpi-grid");
    expect(within(grid).queryByText("Traspasos")).not.toBeInTheDocument();
    expect(within(grid).getByText("Matrícula inicial")).toBeInTheDocument();
    const selector = screen.getByRole("combobox", { name: "Filtrar por tipo de trámite" });
    expect(within(selector).queryByRole("option", { name: "Traspasos" })).not.toBeInTheDocument();
  });
});

describe("Inicio del Gestor — estado general y cola", () => {
  it("el total de la franja suma los estados", async () => {
    render(<GestorDashboard onNewTramite={() => {}} />);
    expect(await screen.findByText("20 trámites en total")).toBeInTheDocument();
  });

  it("pulsar un estado filtra la cola y «Quitar filtro» la devuelve", async () => {
    render(<GestorDashboard onNewTramite={() => {}} />);
    await waitFor(() => expect(mocks.fetchColaInactividad).toHaveBeenCalledTimes(1));
    expect(mocks.fetchColaInactividad).toHaveBeenLastCalledWith(
      { estado: undefined, familia: undefined, limit: 5 },
      expect.any(AbortSignal),
    );

    fireEvent.click(await screen.findByRole("button", { name: /Preparado: 8 trámites/ }));

    await waitFor(() =>
      expect(mocks.fetchColaInactividad).toHaveBeenLastCalledWith(
        { estado: "preparado", familia: undefined, limit: 5 },
        expect.any(AbortSignal),
      ),
    );
    expect(screen.getByRole("link", { name: /Ver todos/ })).toHaveAttribute("href", "/tramites?estado=preparado");

    fireEvent.click(screen.getByRole("button", { name: /Quitar filtro/ }));
    await waitFor(() =>
      expect(mocks.fetchColaInactividad).toHaveBeenLastCalledWith(
        { estado: undefined, familia: undefined, limit: 5 },
        expect.any(AbortSignal),
      ),
    );
    expect(screen.getByRole("link", { name: /Ver todos/ })).toHaveAttribute("href", "/tramites");
  });

  it("el selector de tipo filtra la cola por familia", async () => {
    render(<GestorDashboard onNewTramite={() => {}} />);
    fireEvent.change(await screen.findByRole("combobox", { name: "Filtrar por tipo de trámite" }), {
      target: { value: "traspasos" },
    });

    await waitFor(() =>
      expect(mocks.fetchColaInactividad).toHaveBeenLastCalledWith(
        { estado: undefined, familia: "traspasos", limit: 5 },
        expect.any(AbortSignal),
      ),
    );
  });

  it("cada fila dice los días sin gestión y abre el trámite", async () => {
    render(<GestorDashboard onNewTramite={() => {}} />);
    const cola = await screen.findByTestId("panel-cola");

    expect(await within(cola).findByText("14 días sin gestión")).toBeInTheDocument();
    expect(within(cola).getByText("4 días sin gestión")).toBeInTheDocument();
    expect(within(cola).getByRole("link", { name: "Gestionar FT1-0000001" })).toHaveAttribute(
      "href",
      "/tramites/id-FT1-0000001",
    );
  });
});

describe("Inicio del Gestor — trámites por firma", () => {
  it("sin firmas = el atajo sin_firmas de /tramites; con firmas = borradores − sin firmas", async () => {
    render(<GestorDashboard onNewTramite={() => {}} />);
    const panel = await screen.findByTestId("panel-firma");

    expect(await within(panel).findByText("Borradores con firmas")).toBeInTheDocument();
    expect(within(panel).getByText("Borradores sin firmas")).toBeInTheDocument();
    expect(within(panel).getByText("3")).toBeInTheDocument();
    expect(within(panel).getByText("5")).toBeInTheDocument();
    expect(within(panel).getByText("8")).toBeInTheDocument();
    expect(mocks.searchInstances).toHaveBeenCalledWith(
      expect.objectContaining({ estado: "borrador", busquedaRapida: "sin_firmas", take: 1 }),
    );
    expect(within(panel).getByRole("link", { name: "Ver detalle" })).toHaveAttribute("href", "/tramites?atajo=sin_firmas");
  });

  it("con más borradores que el tope (422) lo dice en palabras, sin cifras", async () => {
    const { TramitesApiError } = await import("@/lib/api/tramites-client");
    mocks.searchInstances.mockRejectedValue(new TramitesApiError(422, "demasiados", null));
    render(<GestorDashboard onNewTramite={() => {}} />);
    const panel = await screen.findByTestId("panel-firma");

    expect(
      await within(panel).findByText("Hay demasiados borradores para contar las firmas; acota el periodo."),
    ).toBeInTheDocument();
    expect(within(panel).queryByText("Borradores con firmas")).not.toBeInTheDocument();
  });
});
