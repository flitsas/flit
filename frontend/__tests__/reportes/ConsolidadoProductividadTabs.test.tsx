// HU #11116 — Tabs Consolidado y Productividad.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  ConsolidadoTab,
  ProductividadV2Tab,
  humanizeActorDimension,
  participationPct,
} from "@/components/atom/modules/_reportes/tabs/ConsolidadoProductividadTabs";
import { ReportFilterProvider } from "@/components/atom/modules/_reportes/ReportFilterContext";
import { ApiError } from "@/lib/api/types";
import { FLIT_EXPORT_JOB_CREATED } from "@/components/atom/modules/_reportes/export-events";

const mocks = vi.hoisted(() => ({
  fetchConsolidado: vi.fn(),
  fetchProductivity: vi.fn(),
  requestExport: vi.fn(),
  usePermissions: vi.fn(),
}));

vi.mock("@/lib/api/reporting-v2", () => ({
  fetchConsolidado: (...a: unknown[]) => mocks.fetchConsolidado(...a),
  fetchProductivity: (...a: unknown[]) => mocks.fetchProductivity(...a),
  requestExport: (...a: unknown[]) => mocks.requestExport(...a),
}));
vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => mocks.usePermissions(),
}));

function renderConsolidado(perms = ["reporting.consolidado", "reporting.export"]) {
  mocks.usePermissions.mockReturnValue({ permissions: perms, isSuperAdmin: false });
  return render(
    <ReportFilterProvider initialSearch="?from=2026-07-01&to=2026-07-30">
      <ConsolidadoTab />
    </ReportFilterProvider>,
  );
}

function renderProductividad(perms = ["reporting.productivity"]) {
  mocks.usePermissions.mockReturnValue({ permissions: perms, isSuperAdmin: false });
  return render(
    <ReportFilterProvider initialSearch="?from=2026-07-01&to=2026-07-30">
      <ProductividadV2Tab />
    </ReportFilterProvider>,
  );
}

const CONSOLIDADO_PAGE = {
  items: [
    {
      dimension: "tipo",
      key: "traslado",
      label: "Traslado",
      total: 80,
      approved: 60,
      rejected: 10,
      inProgress: 10,
    },
    {
      dimension: "tipo",
      key: "matricula",
      label: "Matrícula",
      total: 20,
      approved: 15,
      rejected: 2,
      inProgress: 3,
    },
  ],
  totalGroups: 2,
};

const PRODUCTIVITY_PAGE = {
  items: [
    {
      actorId: null,
      actorLabel: "Ana Pérez",
      dimension: "usuario",
      total: 40,
      approved: 30,
      rejected: 5,
      inProgress: 5,
      avgHours: 12.5,
      minHours: 2,
      maxHours: 40,
    },
    {
      actorId: "ot-1",
      actorLabel: "OT Bogotá",
      dimension: "ot",
      total: 25,
      approved: 20,
      rejected: 2,
      inProgress: 3,
      avgHours: 8,
      minHours: 1,
      maxHours: 20,
    },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.fetchConsolidado.mockResolvedValue(CONSOLIDADO_PAGE);
  mocks.fetchProductivity.mockResolvedValue(PRODUCTIVITY_PAGE);
  mocks.requestExport.mockResolvedValue({
    id: "exp-1",
    status: "pending",
    progressPct: 0,
    format: "csv",
    reportType: "consolidado",
  });
});

describe("helpers HU #11116", () => {
  it("participationPct calcula % con 1 decimal", () => {
    expect(participationPct(80, 100)).toBe(80);
    expect(participationPct(1, 3)).toBe(33.3);
    expect(participationPct(10, 0)).toBe(0);
  });

  it("humanizeActorDimension etiqueta dimensiones", () => {
    expect(humanizeActorDimension("usuario")).toMatch(/Radicador/i);
    expect(humanizeActorDimension("ot")).toMatch(/Organismo/i);
  });
});

describe("ConsolidadoTab AC1", () => {
  it("carga consolidado por tipo con % participación y estado lleno", async () => {
    renderConsolidado();
    expect(screen.getByTestId("consolidado-loading")).toBeInTheDocument();
    await waitFor(() => expect(mocks.fetchConsolidado).toHaveBeenCalled());
    expect(mocks.fetchConsolidado.mock.calls[0]![0]).toMatchObject({
      groupBy: "tipo",
      from: "2026-07-01",
      to: "2026-07-30",
    });
    const table = await screen.findByTestId("consolidado-lleno");
    expect(table).toHaveTextContent("Traslado");
    expect(table).toHaveTextContent("80.0%");
    expect(table).toHaveTextContent("20.0%");
  });

  it("muestra estado error con HTTP", async () => {
    mocks.fetchConsolidado.mockRejectedValueOnce(new ApiError(500, "Error 500"));
    renderConsolidado();
    expect(await screen.findByTestId("consolidado-error")).toHaveTextContent(/HTTP 500/);
  });
});

describe("ProductividadV2Tab AC2", () => {
  it("muestra actores con tipo de actor, OT y conteos ordenados", async () => {
    renderProductividad();
    const table = await screen.findByTestId("productividad-lleno");
    expect(mocks.fetchProductivity).toHaveBeenCalled();
    expect(table).toHaveTextContent("Ana Pérez");
    expect(table).toHaveTextContent("Usuario / Radicador");
    expect(table).toHaveTextContent("OT Bogotá");
    expect(table).toHaveTextContent("Organismo de tránsito");
  });
});

describe("ConsolidadoTab AC3 — export CSV", () => {
  it("POST exports reportType consolidado y dispara evento", async () => {
    const user = userEvent.setup();
    const listener = vi.fn();
    window.addEventListener(FLIT_EXPORT_JOB_CREATED, listener);
    renderConsolidado();
    await screen.findByTestId("consolidado-lleno");
    await user.click(screen.getByTestId("consolidado-export-csv"));
    await waitFor(() => expect(mocks.requestExport).toHaveBeenCalled());
    expect(mocks.requestExport.mock.calls[0]![0]).toMatchObject({
      reportType: "consolidado",
      format: "csv",
      filters: expect.objectContaining({ groupBy: "tipo" }),
    });
    expect(listener).toHaveBeenCalled();
    window.removeEventListener(FLIT_EXPORT_JOB_CREATED, listener);
  });
});

describe("ProductividadV2Tab AC4 — sin permiso", () => {
  it("muestra mensaje canónico sin llamar API", async () => {
    renderProductividad(["reporting.read"]);
    expect(await screen.findByTestId("productividad-no-permiso")).toHaveTextContent(
      "No tienes permiso para ver este reporte",
    );
    expect(mocks.fetchProductivity).not.toHaveBeenCalled();
  });
});

describe("AC5 — vacío contextual", () => {
  it("consolidado vacío", async () => {
    mocks.fetchConsolidado.mockResolvedValue({ items: [], totalGroups: 0 });
    renderConsolidado();
    expect(await screen.findByTestId("consolidado-empty")).toHaveTextContent(
      /Sin datos consolidados/i,
    );
  });

  it("productividad vacío", async () => {
    mocks.fetchProductivity.mockResolvedValue({ items: [] });
    renderProductividad();
    expect(await screen.findByTestId("productividad-empty")).toHaveTextContent(
      /Sin datos de productividad/i,
    );
  });
});
