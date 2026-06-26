import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";

import type { AnalyticsOverviewResponse, CompanyListItem } from "@/lib/api/types";
import { ApiError } from "@/lib/api/types";

// ── Mocks de la capa de datos y del rol (sin red real) ─────────────────────
const mocks = vi.hoisted(() => ({
  fetchAnalyticsOverview: vi.fn(),
  fetchCompaniesIndex: vi.fn(),
  getToken: vi.fn(() => "token"),
  isSuperAdmin: vi.fn(() => false),
}));

vi.mock("@/lib/api/analytics", () => ({ fetchAnalyticsOverview: mocks.fetchAnalyticsOverview }));
vi.mock("@/lib/api/admin-companies", () => ({ fetchCompaniesIndex: mocks.fetchCompaniesIndex }));
vi.mock("@/lib/api/client", () => ({ getToken: mocks.getToken }));
vi.mock("@/lib/auth/jwt", () => ({
  decodeJwtPayload: () => ({ role: "x" }),
  isSuperAdmin: mocks.isSuperAdmin,
}));

import { Reportes } from "@/components/atom/modules/Reportes";

const FULL: AnalyticsOverviewResponse = {
  tenantId: "11111111-1111-1111-1111-111111111111",
  from: "2026-06-01",
  to: "2026-06-26",
  categories: [
    { category: "matriculas", total: 120, byStatus: [{ status: "submitted", count: 80 }, { status: "approved", count: 40 }] },
    { category: "traspasos", total: 30, byStatus: [{ status: "submitted", count: 30 }] },
    { category: "otros", total: 0, byStatus: [] },
  ],
};

const EMPTY: AnalyticsOverviewResponse = {
  tenantId: "11111111-1111-1111-1111-111111111111",
  from: "2026-06-01",
  to: "2026-06-26",
  categories: [
    { category: "matriculas", total: 0, byStatus: [] },
    { category: "traspasos", total: 0, byStatus: [] },
    { category: "otros", total: 0, byStatus: [] },
  ],
};

const COMPANY: CompanyListItem = {
  id: "22222222-2222-2222-2222-222222222222",
  nit: "900123456",
  razonSocial: "Transportes Andinos S.A.S.",
  code: "AND",
  tenantType: "RENTING",
  estadoActivo: true,
  fechaCreacion: "2026-01-01T00:00:00Z",
  rowVersion: 1,
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getToken.mockReturnValue("token");
  mocks.isSuperAdmin.mockReturnValue(false);
  mocks.fetchAnalyticsOverview.mockResolvedValue(FULL);
  mocks.fetchCompaniesIndex.mockResolvedValue({ data: [COMPANY], totalCount: 1, page: 1, pageSize: 100 });
});

describe("Reportes — AC3 estados de UI (UiStateBoundary)", () => {
  it("Cargando: muestra el placeholder accesible antes de la primera respuesta", async () => {
    let resolveFn: (v: AnalyticsOverviewResponse) => void = () => {};
    mocks.fetchAnalyticsOverview.mockReturnValue(
      new Promise<AnalyticsOverviewResponse>((r) => {
        resolveFn = r;
      }),
    );

    render(<Reportes />);

    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();

    resolveFn(FULL);
    await screen.findByText("Total trámites");
  });

  it("Vacío: muestra mensaje cuando no hay trámites en el periodo", async () => {
    mocks.fetchAnalyticsOverview.mockResolvedValue(EMPTY);

    render(<Reportes />);

    expect(await screen.findByTestId("ui-empty")).toHaveTextContent(/no hay trámites/i);
  });

  it("Error: muestra role=alert con reintento y vuelve a consultar al reintentar", async () => {
    mocks.fetchAnalyticsOverview.mockRejectedValueOnce(new ApiError(500, "boom"));

    render(<Reportes />);

    const alert = await screen.findByRole("alert");
    const retry = within(alert).getByRole("button", { name: /reintentar/i });

    mocks.fetchAnalyticsOverview.mockResolvedValue(FULL);
    fireEvent.click(retry);

    expect(await screen.findByText("Total trámites")).toBeInTheDocument();
    expect(mocks.fetchAnalyticsOverview).toHaveBeenCalledTimes(2);
  });

  it("Lleno: pinta el total real y los donuts por categoría", async () => {
    render(<Reportes />);

    expect(await screen.findByText("Total trámites")).toBeInTheDocument();
    // total = 120 + 30 + 0
    expect(screen.getByText("150")).toBeInTheDocument();
    // Títulos de los tres gráficos circulares (también aparecen como tarjeta resumen).
    expect(screen.getAllByText("Matrículas").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Traspasos").length).toBeGreaterThan(0);
    // Leyenda de estados (fuera del SVG, siempre presente).
    expect(screen.getByText("Aprobado")).toBeInTheDocument();
    // Categoría sin datos muestra su nota vacía.
    expect(screen.getByTestId("donut-empty-otros")).toBeInTheDocument();
  });
});

describe("Reportes — AC1 acceso por rol", () => {
  it("Tenant Admin: NO ve el selector de compañía", async () => {
    mocks.isSuperAdmin.mockReturnValue(false);

    render(<Reportes />);

    await screen.findByText("Total trámites");
    expect(screen.queryByLabelText("Compañía")).not.toBeInTheDocument();
    expect(mocks.fetchCompaniesIndex).not.toHaveBeenCalled();
  });

  it("SuperAdmin: ve el selector con sus compañías y filtra por tenantId", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);

    render(<Reportes />);

    const selector = await screen.findByLabelText("Compañía");
    expect(await screen.findByRole("option", { name: "Transportes Andinos S.A.S." })).toBeInTheDocument();

    fireEvent.change(selector, { target: { value: COMPANY.id } });

    await waitFor(() => {
      const lastCall = mocks.fetchAnalyticsOverview.mock.calls.at(-1);
      expect(lastCall?.[0]).toMatchObject({ tenantId: COMPANY.id });
    });
  });
});

describe("Reportes — AC2 filtro de fechas en tiempo real", () => {
  it("al cambiar el rango vuelve a consultar la API con las nuevas fechas", async () => {
    render(<Reportes />);

    await screen.findByText("Total trámites");
    expect(mocks.fetchAnalyticsOverview).toHaveBeenCalledTimes(1);

    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2020-01-01" } });

    await waitFor(() => {
      const lastCall = mocks.fetchAnalyticsOverview.mock.calls.at(-1);
      expect(lastCall?.[0]).toMatchObject({ from: "2020-01-01" });
    });
  });

  it("rango inválido (inicio posterior al fin): muestra error sin llamar a la API de nuevo", async () => {
    render(<Reportes />);

    await screen.findByText("Total trámites");
    expect(mocks.fetchAnalyticsOverview).toHaveBeenCalledTimes(1);

    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2099-12-31" } });

    expect(await screen.findByText(/la fecha inicial no puede ser posterior/i)).toBeInTheDocument();
    expect(mocks.fetchAnalyticsOverview).toHaveBeenCalledTimes(1);
  });
});
