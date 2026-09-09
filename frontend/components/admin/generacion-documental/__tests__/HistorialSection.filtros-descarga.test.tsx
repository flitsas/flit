// HU-03 (Feature #12201) — CF-17/CF-18/CF-19/CF-22: filtros aplicados sobre la petición,
// redescarga presignada, cuatro estados de UI y vacío por filtro.
// Uso de ejemplo: render(<HistorialSection />) con el cliente de API mockeado.
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
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
import { ApiError } from "@/lib/api/types";

const generado = {
  id: "11111111-1111-1111-1111-111111111111",
  documentType: "certificado_rues" as const,
  scenario: null,
  status: "generated" as const,
  companyName: "Renting Demo S.A.S.",
  createdByUserId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  createdByUserName: "Ana Gestora",
  createdAt: "2026-09-01T14:30:00.000Z",
};

const conError = {
  ...generado,
  id: "22222222-2222-2222-2222-222222222222",
  status: "error" as const,
  errorCode: "rues_not_found",
};

function page(items: unknown[], total = items.length) {
  return { items, page: 1, pageSize: 20, total };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.fetchStandaloneDocuments.mockResolvedValue(page([generado]));
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("HistorialSection — filtros (CF-18)", () => {
  it("primera carga: pide la página 1 sin ningún filtro activo", async () => {
    render(<HistorialSection />);

    await waitFor(() => expect(mocks.fetchStandaloneDocuments).toHaveBeenCalled());
    const [params] = mocks.fetchStandaloneDocuments.mock.calls[0];
    expect(params).toMatchObject({ page: 1, pageSize: 20 });
    expect(params.documentType).toBeUndefined();
    expect(params.status).toBeUndefined();
    expect(params.dateFrom).toBeUndefined();
    expect(params.userId).toBeUndefined();
  });

  it("filtrar por tipo, rango de fechas y usuario aplica los tres a la misma petición", async () => {
    render(<HistorialSection />);
    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());

    await userEvent.selectOptions(screen.getByLabelText("Tipo de documento"), "certificado_rues");
    await waitFor(() => expect(mocks.fetchStandaloneDocuments).toHaveBeenCalledTimes(2));

    await userEvent.type(screen.getByLabelText("Desde"), "2026-09-01");
    await userEvent.type(screen.getByLabelText("Hasta"), "2026-09-09");
    await userEvent.selectOptions(screen.getByLabelText("Usuario"), generado.createdByUserId);

    await waitFor(() => {
      const last = mocks.fetchStandaloneDocuments.mock.calls.at(-1)![0];
      expect(last).toMatchObject({
        documentType: "certificado_rues",
        dateFrom: "2026-09-01",
        dateTo: "2026-09-09",
        userId: generado.createdByUserId,
      });
    });
  });

  it("«En proceso» viaja como los DOS estados internos que cubre (CF-21)", async () => {
    render(<HistorialSection />);
    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());

    await userEvent.selectOptions(screen.getByLabelText("Estado"), "en_proceso");

    await waitFor(() => {
      const last = mocks.fetchStandaloneDocuments.mock.calls.at(-1)![0];
      expect(last.status).toEqual(["pending", "processing"]);
    });
  });

  it("«Generado» viaja como un único estado interno", async () => {
    render(<HistorialSection />);
    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());

    await userEvent.selectOptions(screen.getByLabelText("Estado"), "generated");

    await waitFor(() => {
      const last = mocks.fetchStandaloneDocuments.mock.calls.at(-1)![0];
      expect(last.status).toEqual(["generated"]);
    });
  });

  it("cambiar un filtro vuelve a la página 1", async () => {
    // El backend hace eco de la página pedida; así se ve que el cambio de filtro la reinicia.
    mocks.fetchStandaloneDocuments.mockImplementation((params: { page?: number }) =>
      Promise.resolve({ items: [generado], page: params.page ?? 1, pageSize: 20, total: 60 }),
    );
    render(<HistorialSection />);
    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());

    await userEvent.click(screen.getByRole("button", { name: "Página siguiente" }));
    await waitFor(() =>
      expect(mocks.fetchStandaloneDocuments.mock.calls.at(-1)![0].page).toBe(2),
    );

    await userEvent.selectOptions(screen.getByLabelText("Estado"), "error");

    await waitFor(() => {
      const last = mocks.fetchStandaloneDocuments.mock.calls.at(-1)![0];
      expect(last.page).toBe(1);
    });
  });
});

describe("HistorialSection — cuatro estados de UI (CF-22)", () => {
  it("vacío sin filtros: ofrece generar el primer documento, no una tabla vacía", async () => {
    mocks.fetchStandaloneDocuments.mockResolvedValue(page([]));
    render(<HistorialSection />);

    await waitFor(() => expect(screen.getByTestId("ui-empty")).toBeInTheDocument());
    expect(screen.getByText(/aún no has generado documentos/i)).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Generar el primer documento" }));
    expect(mocks.push).toHaveBeenCalledWith("/admin/generacion-documental");
  });

  it("vacío POR FILTRO: lo explica y ofrece limpiar los filtros", async () => {
    render(<HistorialSection />);
    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());

    mocks.fetchStandaloneDocuments.mockResolvedValue(page([]));
    await userEvent.selectOptions(screen.getByLabelText("Estado"), "error");

    await waitFor(() => expect(screen.getByTestId("ui-empty")).toBeInTheDocument());
    expect(screen.getByText(/ningún documento coincide con los filtros/i)).toBeInTheDocument();

    mocks.fetchStandaloneDocuments.mockResolvedValue(page([generado]));
    await userEvent.click(
      screen.getByTestId("ui-empty").querySelector("button")!,
    );
    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());
  });

  it("cargando: skeleton mientras la petición está en vuelo", () => {
    mocks.fetchStandaloneDocuments.mockReturnValue(new Promise(() => {}));
    render(<HistorialSection />);
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
  });

  it("error: alerta con reintento que vuelve a pedir el listado", async () => {
    mocks.fetchStandaloneDocuments.mockRejectedValue(new Error("boom"));
    render(<HistorialSection />);

    await waitFor(() => expect(screen.getByTestId("ui-error")).toBeInTheDocument());

    mocks.fetchStandaloneDocuments.mockResolvedValue(page([generado]));
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());
  });

  it("lleno: pinta la metadata de la fila y su etiqueta de estado (CF-17)", async () => {
    render(<HistorialSection />);

    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());
    // Acotado a la tabla: «Generado» también es una opción del selector de estado.
    const tabla = within(screen.getByRole("table"));
    expect(tabla.getByText("Certificado RUES")).toBeInTheDocument();
    expect(tabla.getByText("Renting Demo S.A.S.")).toBeInTheDocument();
    expect(tabla.getByText("Ana Gestora")).toBeInTheDocument();
    expect(tabla.getByText("Generado")).toBeInTheDocument();
  });
});

describe("HistorialSection — redescarga (CF-19)", () => {
  it("descargar pide la presigned URL de esa fila y abre el PDF", async () => {
    const open = vi.spyOn(window, "open").mockImplementation(() => null);
    mocks.requestStandaloneDocumentDownload.mockResolvedValue({
      url: "https://storage.example/doc.pdf?X-Amz-Signature=abc",
      expiresAt: "2026-09-09T10:10:00.000Z",
    });

    render(<HistorialSection />);
    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());

    await userEvent.click(screen.getByRole("button", { name: /descargar certificado rues/i }));

    expect(mocks.requestStandaloneDocumentDownload).toHaveBeenCalledWith(generado.id);
    await waitFor(() => expect(open).toHaveBeenCalled());
    expect(open.mock.calls[0][0]).toContain("https://storage.example/doc.pdf");
  });

  it("no loguea la URL firmada en consola", async () => {
    const log = vi.spyOn(console, "log").mockImplementation(() => {});
    const info = vi.spyOn(console, "info").mockImplementation(() => {});
    const error = vi.spyOn(console, "error").mockImplementation(() => {});
    vi.spyOn(window, "open").mockImplementation(() => null);
    mocks.requestStandaloneDocumentDownload.mockResolvedValue({
      url: "https://storage.example/doc.pdf?X-Amz-Signature=SECRETO",
      expiresAt: "2026-09-09T10:10:00.000Z",
    });

    render(<HistorialSection />);
    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());
    await userEvent.click(screen.getByRole("button", { name: /descargar certificado rues/i }));

    await waitFor(() => expect(mocks.requestStandaloneDocumentDownload).toHaveBeenCalled());
    for (const spy of [log, info, error]) {
      for (const call of spy.mock.calls) {
        expect(JSON.stringify(call)).not.toContain("X-Amz-Signature");
      }
    }
  });

  it("un 409 se explica sin ofrecer descarga: el documento no está generado", async () => {
    mocks.requestStandaloneDocumentDownload.mockRejectedValue(new ApiError(409, "conflict"));

    render(<HistorialSection />);
    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());
    await userEvent.click(screen.getByRole("button", { name: /descargar certificado rues/i }));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(/aún no está generado/i));
  });

  it("un 404 no revela nada del documento", async () => {
    mocks.requestStandaloneDocumentDownload.mockRejectedValue(new ApiError(404, "not_found"));

    render(<HistorialSection />);
    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());
    await userEvent.click(screen.getByRole("button", { name: /descargar certificado rues/i }));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(/no encontramos ese documento/i));
  });

  it("una fila en error no ofrece la acción de descarga", async () => {
    mocks.fetchStandaloneDocuments.mockResolvedValue(page([conError]));
    render(<HistorialSection />);

    await waitFor(() => expect(screen.getByRole("table")).toBeInTheDocument());
    const tabla = within(screen.getByRole("table"));
    expect(tabla.getByText("Error")).toBeInTheDocument();
    expect(tabla.queryByRole("button", { name: /descargar/i })).not.toBeInTheDocument();
  });
});
