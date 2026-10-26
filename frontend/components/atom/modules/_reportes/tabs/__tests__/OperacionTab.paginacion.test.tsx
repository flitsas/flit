// HU #13923 — la tabla «Trámites atascados» usa el DataTable estándar: 10 filas por página con la
// paginación de FLIT («Filas por página») y conserva su texto vacío cuando no hay filas.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";

import type { FunnelResponse, OtMetricsResponse, StuckItem } from "@/lib/api/analytics-v2";

const mocks = vi.hoisted(() => ({ fetchFunnel: vi.fn(), fetchOtMetrics: vi.fn() }));

vi.mock("@/lib/api/analytics-v2", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/analytics-v2")>();
  return { ...actual, fetchFunnel: mocks.fetchFunnel, fetchOtMetrics: mocks.fetchOtMetrics };
});

import { OperacionTab } from "@/components/atom/modules/_reportes/tabs/OperacionTab";
import { defaultFilters } from "@/components/atom/modules/_reportes/filters";

const FUNNEL: FunnelResponse = {
  current: {
    states: [{ stage: "borrador", count: 1, pctOfFirst: 100, pctOfPrev: 100 }],
    anulados: 0,
    rechazadosVigentes: 0,
    wizardSteps: [],
  },
  previous: null,
  comparison: null,
};

function stuckItems(n: number): StuckItem[] {
  return Array.from({ length: n }, (_, i) => ({
    instanceId: `inst-${i + 1}`,
    referenceNumber: `TRM-${String(i + 1).padStart(6, "0")}`,
    status: "entregado",
    daysInStatus: 9,
    transitOfficeName: "OT Bogotá",
    procedureTypeName: "Traspaso",
    createdByDisplayName: "Ana Pérez",
  }));
}

function metricsConAtascados(items: StuckItem[]): OtMetricsResponse {
  return {
    current: {
      summary: {
        entregados: 1,
        aprobados: 1,
        rechazados: 0,
        rejectionRatePct: 0,
        avgApprovalHours: 1,
        p50ApprovalHours: 1,
        p90ApprovalHours: 1,
        reincidencePct: 0,
        stuckCount: items.length,
      },
      rejectionByOffice: [],
      rejectionByReason: [],
      rejectionByType: [],
      approvalTimesByOffice: [],
      officeRanking: [],
      reincidence: { rechazadas: 0, reintentadas: 0, avgCiclos: 0, maxCiclos: 0 },
      stuck: { totalCount: items.length, items },
      rejectionByReasonCatalog: [],
      avgReasonsPerRejection: 0,
      internalCycle: { avgHours: null, p50Hours: null, p90Hours: null },
    },
    previous: null,
    comparison: null,
  };
}

describe("OperacionTab — tabla de atascados con la paginación estándar", () => {
  beforeEach(() => {
    sessionStorage.clear();
    mocks.fetchFunnel.mockReset().mockResolvedValue(FUNNEL);
    mocks.fetchOtMetrics.mockReset();
  });

  it("con 15 filas muestra 10, el resumen y el selector «Filas por página»", async () => {
    mocks.fetchOtMetrics.mockResolvedValue(metricsConAtascados(stuckItems(15)));
    render(<OperacionTab filters={defaultFilters()} needsCompany={false} onDrillDown={() => {}} />);

    const tabla = await screen.findByTestId("stuck-table");
    await waitFor(() => expect(within(tabla).getAllByRole("link")).toHaveLength(10));
    expect(within(tabla).getByText("Mostrando 1–10 de 15")).toBeInTheDocument();
    expect(within(tabla).getByRole("combobox", { name: "Filas por página" })).toHaveValue("10");

    // Opciones fijas del estándar.
    const opciones = within(within(tabla).getByRole("combobox", { name: "Filas por página" }))
      .getAllByRole("option")
      .map((o) => o.textContent);
    expect(opciones).toEqual(["10", "20", "30", "50"]);

    // Cambiar a 20 muestra las 15.
    fireEvent.change(within(tabla).getByRole("combobox", { name: "Filas por página" }), {
      target: { value: "20" },
    });
    await waitFor(() => expect(within(tabla).getAllByRole("link")).toHaveLength(15));
  });

  it("con 0 filas conserva su texto vacío", async () => {
    mocks.fetchOtMetrics.mockResolvedValue(metricsConAtascados([]));
    render(<OperacionTab filters={defaultFilters()} needsCompany={false} onDrillDown={() => {}} />);

    expect(await screen.findByText("No hay trámites atascados. ¡Buen ritmo!")).toBeInTheDocument();
  });
});
