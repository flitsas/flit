// HU #12211 (Feature #12201) — CF-18 en I3: el filtro por lote del historial y su convivencia
// con los de tipo, fecha y usuario.
// Uso de ejemplo: render(<HistorialSection />) con el cliente de API mockeado.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
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

const BATCH_ID = "0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee";

const fila = {
  id: "11111111-1111-1111-1111-111111111111",
  documentType: "certificado_rues" as const,
  scenario: null,
  status: "generated" as const,
  companyName: "Renting Demo S.A.S.",
  createdByUserId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  createdByUserName: "Ana Gestora",
  createdAt: "2026-09-01T14:30:00.000Z",
};

function page(items: unknown[], total = items.length) {
  return { items, page: 1, pageSize: 20, total };
}

/** Última llamada al listado. Es donde se comprueba qué filtros viajaron de verdad. */
function ultimaConsulta() {
  const llamadas = mocks.fetchStandaloneDocuments.mock.calls;
  return llamadas[llamadas.length - 1][0];
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.fetchStandaloneDocuments.mockResolvedValue(page([fila]));
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("HistorialSection — filtro por lote (CF-18)", () => {
  it("envía batchId al listado cuando se escribe un identificador de lote", async () => {
    const user = userEvent.setup();
    render(<HistorialSection />);

    await waitFor(() => expect(mocks.fetchStandaloneDocuments).toHaveBeenCalled());

    await user.type(screen.getByLabelText(/^lote$/i), BATCH_ID);

    await waitFor(() => expect(ultimaConsulta()).toMatchObject({ batchId: BATCH_ID }));
  });

  it("sin lote escrito, batchId no viaja en la consulta", async () => {
    render(<HistorialSection />);

    await waitFor(() => expect(mocks.fetchStandaloneDocuments).toHaveBeenCalled());

    expect(ultimaConsulta().batchId).toBeUndefined();
  });

  it("el filtro de lote CONVIVE con los de tipo, fecha y usuario", async () => {
    const user = userEvent.setup();
    render(<HistorialSection />);

    await waitFor(() => expect(mocks.fetchStandaloneDocuments).toHaveBeenCalled());

    await user.selectOptions(screen.getByLabelText(/tipo de documento/i), "certificado_rues");
    await user.type(screen.getByLabelText(/^desde$/i), "2026-09-01");
    await user.type(screen.getByLabelText(/^hasta$/i), "2026-09-09");
    await user.type(screen.getByLabelText(/^lote$/i), BATCH_ID);

    // El selector de usuario se puebla con los autores ya vistos en el historial.
    await waitFor(() =>
      expect(screen.getByLabelText(/^usuario$/i)).not.toHaveAttribute("disabled"),
    );
    await user.selectOptions(screen.getByLabelText(/^usuario$/i), fila.createdByUserId);

    await waitFor(() =>
      expect(ultimaConsulta()).toMatchObject({
        documentType: "certificado_rues",
        dateFrom: "2026-09-01",
        dateTo: "2026-09-09",
        userId: fila.createdByUserId,
        batchId: BATCH_ID,
      }),
    );
  });

  it("con un lote filtrado ofrece el paso al seguimiento de ese lote", async () => {
    const user = userEvent.setup();
    render(<HistorialSection />);

    await waitFor(() => expect(mocks.fetchStandaloneDocuments).toHaveBeenCalled());

    expect(screen.queryByRole("button", { name: /ver seguimiento/i })).not.toBeInTheDocument();

    await user.type(screen.getByLabelText(/^lote$/i), BATCH_ID);

    const boton = await screen.findByRole("button", { name: /ver seguimiento de este lote/i });
    await user.click(boton);

    expect(mocks.push).toHaveBeenCalledWith(`/admin/generacion-documental/lotes/${BATCH_ID}`);
  });

  it("«Limpiar filtros» también borra el lote", async () => {
    const user = userEvent.setup();
    mocks.fetchStandaloneDocuments.mockResolvedValue(page([]));
    render(<HistorialSection />);

    await waitFor(() => expect(mocks.fetchStandaloneDocuments).toHaveBeenCalled());

    await user.type(screen.getByLabelText(/^lote$/i), BATCH_ID);
    await waitFor(() => expect(ultimaConsulta().batchId).toBe(BATCH_ID));

    await user.click(screen.getAllByRole("button", { name: /limpiar filtros/i })[0]);

    await waitFor(() => expect(ultimaConsulta().batchId).toBeUndefined());
  });
});
