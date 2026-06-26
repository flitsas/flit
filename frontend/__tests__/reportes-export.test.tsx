import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";

import { ApiError } from "@/lib/api/types";

// ── Mock de las funciones de exportación (sin red ni descarga real) ─────────
const mocks = vi.hoisted(() => ({
  exportAnalyticsExcel: vi.fn(),
  exportExecutivePdf: vi.fn(),
}));

vi.mock("@/lib/api/analytics", () => ({
  exportAnalyticsExcel: mocks.exportAnalyticsExcel,
  exportExecutivePdf: mocks.exportExecutivePdf,
}));

import { ExportButtons } from "@/components/atom/modules/_reportes/ExportButtons";

const RANGE = { from: "2026-06-01", to: "2026-06-26" };

beforeEach(() => {
  vi.clearAllMocks();
  mocks.exportAnalyticsExcel.mockResolvedValue(undefined);
  mocks.exportExecutivePdf.mockResolvedValue(undefined);
});

describe("ExportButtons — AC1 Excel con filtros + progreso", () => {
  it("exporta Excel con el rango y la categoría activos y muestra progreso", async () => {
    let resolveFn: () => void = () => {};
    mocks.exportAnalyticsExcel.mockReturnValue(new Promise<void>((r) => (resolveFn = r)));

    render(<ExportButtons range={RANGE} category="matriculas" status="submitted" tenantId="t-1" />);

    const button = screen.getByRole("button", { name: /exportar excel/i });
    fireEvent.click(button);

    // Indicador de progreso (AC1): el botón pasa a aria-busy y muestra "Exportando…".
    expect(await screen.findByRole("button", { name: /exportando/i })).toHaveAttribute("aria-busy", "true");

    resolveFn();

    await waitFor(() => {
      expect(mocks.exportAnalyticsExcel).toHaveBeenCalledWith(
        expect.objectContaining({ from: "2026-06-01", to: "2026-06-26", category: "matriculas", status: "submitted", tenantId: "t-1" }),
      );
    });
    // Vuelve al estado normal tras terminar.
    expect(await screen.findByRole("button", { name: /exportar excel/i })).toBeInTheDocument();
  });
});

describe("ExportButtons — AC2 PDF ejecutivo", () => {
  it("genera el PDF con el rango del dashboard", async () => {
    render(<ExportButtons range={RANGE} tenantId="t-1" />);

    fireEvent.click(screen.getByRole("button", { name: /resumen ejecutivo pdf/i }));

    await waitFor(() => {
      expect(mocks.exportExecutivePdf).toHaveBeenCalledWith(
        expect.objectContaining({ from: "2026-06-01", to: "2026-06-26", tenantId: "t-1" }),
      );
    });
  });
});

describe("ExportButtons — AC3 error de exportación accesible", () => {
  it("muestra mensaje role=alert sin bloquear el dashboard cuando la API falla", async () => {
    mocks.exportExecutivePdf.mockRejectedValueOnce(new ApiError(500, "boom"));

    render(<ExportButtons range={RANGE} />);

    fireEvent.click(screen.getByRole("button", { name: /resumen ejecutivo pdf/i }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(/no se pudo generar la exportación/i);
    // El botón vuelve a estar habilitado (no bloquea la pantalla).
    expect(screen.getByRole("button", { name: /resumen ejecutivo pdf/i })).not.toBeDisabled();
  });

  it("mensaje específico para rango inválido (400)", async () => {
    mocks.exportAnalyticsExcel.mockRejectedValueOnce(new ApiError(400, "bad range"));

    render(<ExportButtons range={RANGE} />);

    fireEvent.click(screen.getByRole("button", { name: /exportar excel/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/rango de fechas no es válido para exportar/i);
  });
});
