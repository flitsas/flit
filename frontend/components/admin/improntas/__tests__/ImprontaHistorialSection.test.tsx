// HU #10470 — Vista de historial de improntas generadas (listado filtrable).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ImprontaHistorialSection } from "../ImprontaHistorialSection";
import type { ImprontaHistorialItem } from "@/lib/api/types-improntas";

vi.mock("@/lib/api/admin-improntas", () => ({
  fetchImprontasHistorial: vi.fn(),
}));

import { fetchImprontasHistorial } from "@/lib/api/admin-improntas";

const item: ImprontaHistorialItem = {
  id: "impronta-1",
  radicado: "IMPR-00000001",
  placa: "ABC123",
  numMotor: "MTR-1",
  orgNombre: "Renting Demo S.A.S.",
  orgNit: "900123456-7",
  orgCiudad: "Bogotá D.C.",
  operador: "Ana Operadora",
  hash: "abc123hash",
  fechaImpresa: "2026-06-30T15:04:00Z",
  flitUserName: "Ana Operadora",
  tenantId: "tenant-1",
  createdAt: "2026-06-30T15:04:00Z",
};

describe("ImprontaHistorialSection — HU #10470", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC1 muestra tabla paginada con radicado, placa, fecha, operador y usuario FLIT", async () => {
    vi.mocked(fetchImprontasHistorial).mockResolvedValue({
      data: [item],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });

    render(<ImprontaHistorialSection />);

    expect(await screen.findByText("IMPR-00000001")).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "ABC123" })).toBeInTheDocument();
    expect(screen.getAllByText("Ana Operadora").length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText(/Mostrando 1–1 de 1/)).toBeInTheDocument();
    expect(fetchImprontasHistorial).toHaveBeenCalledWith(
      expect.objectContaining({ page: 1, pageSize: 20 }),
      expect.anything(),
    );
  });

  it("AC2 muestra un estado de error específico ante fallo del backend, distinguible de vacío", async () => {
    vi.mocked(fetchImprontasHistorial).mockRejectedValue(new Error("network down"));

    render(<ImprontaHistorialSection />);

    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();
    expect(
      screen.getByText(/No se pudo cargar el historial de improntas/i),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("ui-empty")).not.toBeInTheDocument();
  });

  it("AC3 sin filtro y sin datos muestra el mensaje de historial vacío", async () => {
    vi.mocked(fetchImprontasHistorial).mockResolvedValue({
      data: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
    });

    render(<ImprontaHistorialSection />);

    expect(await screen.findByTestId("ui-empty")).toBeInTheDocument();
    expect(
      screen.getByText(/Aún no hay improntas generadas para este tenant\./i),
    ).toBeInTheDocument();
  });

  it("AC3 filtra por placa y rango de fechas sin resultados: estado vacío explícito distinto del inicial", async () => {
    vi.mocked(fetchImprontasHistorial)
      .mockResolvedValueOnce({ data: [item], totalCount: 1, page: 1, pageSize: 20 })
      .mockResolvedValueOnce({ data: [], totalCount: 0, page: 1, pageSize: 20 });

    const user = userEvent.setup();
    render(<ImprontaHistorialSection />);

    await screen.findByText("IMPR-00000001");

    await user.type(screen.getByLabelText(/^Placa$/i), "zzz999");
    await user.type(screen.getByLabelText(/^Desde$/i), "2026-01-01");
    await user.type(screen.getByLabelText(/^Hasta$/i), "2026-01-31");
    await user.click(screen.getByRole("button", { name: /Aplicar filtros/i }));

    expect(await screen.findByTestId("ui-empty")).toBeInTheDocument();
    expect(screen.getByText(/Sin resultados para este filtro\./i)).toBeInTheDocument();

    await waitFor(() =>
      expect(fetchImprontasHistorial).toHaveBeenLastCalledWith(
        expect.objectContaining({
          placa: "ZZZ999",
          dateFrom: "2026-01-01T00:00:00.000Z",
          dateTo: "2026-01-31T23:59:59.999Z",
          page: 1,
        }),
        expect.anything(),
      ),
    );
  });

  it("pagina hacia la siguiente página conservando los filtros aplicados", async () => {
    // El backend real "hace eco" de la página solicitada (como fetchOtClientProcedures);
    // se simula así para no enmascarar el bug de que la UI corrija page al valor del server.
    vi.mocked(fetchImprontasHistorial).mockImplementation((params) =>
      Promise.resolve({
        data: [item],
        totalCount: 40,
        page: params?.page ?? 1,
        pageSize: 20,
      }),
    );

    const user = userEvent.setup();
    render(<ImprontaHistorialSection />);

    await screen.findByText("IMPR-00000001");
    await user.click(screen.getByRole("button", { name: /Página siguiente/i }));

    await waitFor(() =>
      expect(fetchImprontasHistorial).toHaveBeenLastCalledWith(
        expect.objectContaining({ page: 2 }),
        expect.anything(),
      ),
    );
    expect(await screen.findByText("2 / 2")).toBeInTheDocument();
  });
});
