// HU-01 (Feature #12201) — CF-22: los cuatro estados de UI del historial, con el estado
// vacío ofreciendo la acción de generar el primer documento.
// Uso de ejemplo: render(<HistorialSection />) con `fetchStandaloneDocuments` mockeado.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const mocks = vi.hoisted(() => ({
  fetchStandaloneDocuments: vi.fn(),
  requestStandaloneDocumentDownload: vi.fn(),
  push: vi.fn(),
}));

vi.mock("@/lib/api/admin-generacion-documental", () => ({
  fetchStandaloneDocuments: mocks.fetchStandaloneDocuments,
  requestStandaloneDocumentDownload: mocks.requestStandaloneDocumentDownload,
}));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: mocks.push }) }));

import { HistorialSection } from "../HistorialSection";

const item = {
  id: "11111111-1111-1111-1111-111111111111",
  documentType: "certificado_rues" as const,
  scenario: null,
  status: "generated" as const,
  companyName: "Renting Demo S.A.S.",
  createdByUserName: "Ana Gestora",
  createdAt: "2026-09-01T14:30:00.000Z",
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe("HistorialSection — cuatro estados de UI (CF-22)", () => {
  it("cargando: muestra el skeleton mientras la petición está en vuelo", () => {
    mocks.fetchStandaloneDocuments.mockReturnValue(new Promise(() => {}));
    render(<HistorialSection />);
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
  });

  it("vacío: explica la ausencia y ofrece generar el primer documento (no una tabla vacía)", async () => {
    mocks.fetchStandaloneDocuments.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
    render(<HistorialSection />);

    await waitFor(() => expect(screen.getByTestId("ui-empty")).toBeInTheDocument());
    expect(screen.getByText(/aún no has generado documentos/i)).toBeInTheDocument();

    const cta = screen.getByRole("button", { name: "Generar el primer documento" });
    await userEvent.click(cta);
    expect(mocks.push).toHaveBeenCalledWith("/admin/generacion-documental");
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("error: mensaje con role=alert y reintento que vuelve a pedir el listado", async () => {
    mocks.fetchStandaloneDocuments.mockRejectedValue(new Error("boom"));
    render(<HistorialSection />);

    await waitFor(() => expect(screen.getByTestId("ui-error")).toBeInTheDocument());
    expect(screen.getByText(/no se pudo cargar el historial/i)).toBeInTheDocument();

    mocks.fetchStandaloneDocuments.mockResolvedValue({ items: [item], page: 1, pageSize: 20, total: 1 });
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());
  });

  it("lleno: renderiza la tabla con la fila devuelta y su etiqueta de estado", async () => {
    mocks.fetchStandaloneDocuments.mockResolvedValue({ items: [item], page: 1, pageSize: 20, total: 1 });
    render(<HistorialSection />);

    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());
    // HU-03 añadió el selector de estado, que también dice «Generado»: se acota a la tabla.
    expect(within(screen.getByRole("table")).getByText("Generado")).toBeInTheDocument();
  });

  it("contrato: pide la primera página con el tamaño de página del módulo", async () => {
    mocks.fetchStandaloneDocuments.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
    render(<HistorialSection />);

    await waitFor(() => expect(mocks.fetchStandaloneDocuments).toHaveBeenCalled());
    // HU-03 añadió los filtros: la primera carga los manda sin valor, no los omite del objeto.
    expect(mocks.fetchStandaloneDocuments).toHaveBeenCalledWith(
      expect.objectContaining({ page: 1, pageSize: 20 }),
      expect.any(AbortSignal),
    );
  });

  it("no rompe si el backend omite items/total (respuesta degradada)", async () => {
    mocks.fetchStandaloneDocuments.mockResolvedValue({ page: 1, pageSize: 20 });
    render(<HistorialSection />);
    await waitFor(() => expect(screen.getByTestId("ui-empty")).toBeInTheDocument());
  });
});
