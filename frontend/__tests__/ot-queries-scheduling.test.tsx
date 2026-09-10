// "Programar este informe" en Consultas personalizadas del organismo (Reportes 2.0, HU-D, tercera
// ola). Antes de esta HU, `OtQueriesTab` no recibía `onSchedule` y el botón de calendario de
// `SavedQueryList` simplemente no se renderizaba — la brecha que corrigió el PR #275.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type { OtQueryField, OtSavedQuery } from "@/lib/api/ot-queries";

const mocks = vi.hoisted(() => ({
  fetchOtQueryFields: vi.fn(),
  fetchOtSavedQueries: vi.fn(),
  runOtQuery: vi.fn(),
  saveOtQuery: vi.fn(),
  deleteOtSavedQuery: vi.fn(),
}));

vi.mock("@/lib/api/ot-queries", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/ot-queries")>();
  return {
    ...actual,
    fetchOtQueryFields: mocks.fetchOtQueryFields,
    fetchOtSavedQueries: mocks.fetchOtSavedQueries,
    runOtQuery: mocks.runOtQuery,
    saveOtQuery: mocks.saveOtQuery,
    deleteOtSavedQuery: mocks.deleteOtSavedQuery,
  };
});

import { OtQueriesTab } from "@/components/admin/transit-offices/_reportes/OtQueriesTab";

const FIELDS: OtQueryField[] = [];

const SAVED_QUERY: OtSavedQuery = {
  id: "q-1",
  nombre: "Solo traspasos",
  descripcion: null,
  deFabrica: false,
  definition: {
    fechas: { campo: "radicacion", preset: "ultimos_30" },
    condiciones: [],
    columnas: ["referencia", "placa"],
  },
  createdAt: "2026-08-01T10:00:00Z",
  updatedAt: null,
};

describe("OtQueriesTab — Programar este informe (savedQueryScope=\"ot\")", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchOtQueryFields.mockResolvedValue(FIELDS);
    mocks.fetchOtSavedQueries.mockResolvedValue([SAVED_QUERY]);
  });

  it("no muestra el botón de calendario cuando no se pasa onScheduleQuery", async () => {
    render(<OtQueriesTab transitOfficeId="ot-1" />);

    await waitFor(() => expect(screen.getByText("Solo traspasos")).toBeInTheDocument());

    expect(
      screen.queryByRole("button", { name: /Programar informe de Solo traspasos/i }),
    ).not.toBeInTheDocument();
  });

  it("muestra el botón de calendario y llama onScheduleQuery con la consulta guardada al hacer clic", async () => {
    const onScheduleQuery = vi.fn();
    const user = userEvent.setup();

    render(<OtQueriesTab transitOfficeId="ot-1" onScheduleQuery={onScheduleQuery} />);

    await waitFor(() => expect(screen.getByText("Solo traspasos")).toBeInTheDocument());

    const boton = screen.getByRole("button", { name: /Programar informe de Solo traspasos/i });
    await user.click(boton);

    expect(onScheduleQuery).toHaveBeenCalledTimes(1);
    expect(onScheduleQuery).toHaveBeenCalledWith(expect.objectContaining({ id: "q-1", nombre: "Solo traspasos" }));
  });

  it("no lanza cuando onScheduleQuery es undefined y solo hay consultas de fábrica", async () => {
    mocks.fetchOtSavedQueries.mockResolvedValue([]);

    expect(() => render(<OtQueriesTab transitOfficeId="ot-1" />)).not.toThrow();
    await waitFor(() => expect(mocks.fetchOtSavedQueries).toHaveBeenCalled());
  });
});
