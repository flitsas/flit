// HU #10523 (RF31) — panel de parámetros documentales por gestora. API mockeada.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyDocumentParamsPanel } from "../CompanyDocumentParamsPanel";
import type { CompanyDocumentParam } from "@/lib/api/admin-company-document-params";
import { ApiError } from "@/lib/api/types";

vi.mock("@/lib/api/admin-company-document-params", () => ({
  fetchCompanyDocumentParams: vi.fn(),
  upsertCompanyDocumentParam: vi.fn(),
}));
vi.mock("@/lib/api/admin-document-types", () => ({
  fetchDocumentTypes: vi.fn(),
}));

import {
  fetchCompanyDocumentParams,
  upsertCompanyDocumentParam,
} from "@/lib/api/admin-company-document-params";
import { fetchDocumentTypes } from "@/lib/api/admin-document-types";

const TENANT = "aaaaaaaa-0000-4000-8000-000000000001";

function param(code: string, state: CompanyDocumentParam["state"]): CompanyDocumentParam {
  return { id: `id-${code}`, documentTypeCode: code, state };
}

describe("CompanyDocumentParamsPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchDocumentTypes).mockRejectedValue(new Error("sin catálogo"));
  });

  it("muestra el estado de carga", () => {
    vi.mocked(fetchCompanyDocumentParams).mockReturnValue(new Promise(() => {}));
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);
    expect(screen.getByText(/cargando parámetros documentales/i)).toBeInTheDocument();
  });

  it("lista los parámetros con nombre legible, código y badge de estado", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([param("soat", "OBLIGATORIO"), param("cepd", "OCULTO")]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    expect(await screen.findByText("soat")).toBeInTheDocument();
    expect(screen.getByText("SOAT")).toBeInTheDocument();
    expect(screen.getByText("Obligatorio")).toBeInTheDocument();
    expect(screen.getByText("Oculto")).toBeInTheDocument();
    expect(fetchCompanyDocumentParams).toHaveBeenCalledWith(TENANT, expect.anything(), undefined);
  });

  it("muestra el vacío sin parámetros", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    expect(await screen.findByText(/se aplica el comportamiento base/i)).toBeInTheDocument();
  });

  it("muestra el motivo real del error (403) y reintenta", async () => {
    vi.mocked(fetchCompanyDocumentParams)
      .mockRejectedValueOnce(new ApiError(403, "No se pudo completar la solicitud. Inténtalo de nuevo."))
      .mockResolvedValueOnce([param("soat", "OPCIONAL")]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    expect(await screen.findByText(/no tienes permiso para ver los parámetros documentales/i)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByText("soat")).toBeInTheDocument();
    expect(fetchCompanyDocumentParams).toHaveBeenCalledTimes(2);
  });

  it("distingue un 500 y una respuesta con formato inesperado", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockRejectedValueOnce(new ApiError(500, "x"));
    const { unmount } = render(<CompanyDocumentParamsPanel tenantId={TENANT} />);
    expect(await screen.findByText(/falló al cargar los parámetros \(error 500\)/i)).toBeInTheDocument();
    unmount();

    vi.mocked(fetchCompanyDocumentParams).mockResolvedValueOnce({} as never);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);
    expect(await screen.findByText(/no tiene el formato esperado/i)).toBeInTheDocument();
  });

  it("edita el estado de un documento desde el modal (upsert)", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([param("soat", "OBLIGATORIO")]);
    vi.mocked(upsertCompanyDocumentParam).mockResolvedValue(param("soat", "OCULTO"));
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    await userEvent.click(await screen.findByRole("button", { name: "Editar soat" }));
    const dialog = screen.getByRole("dialog");
    await userEvent.click(within(dialog).getByRole("radio", { name: /oculto/i }));
    await userEvent.click(within(dialog).getByRole("button", { name: "Guardar" }));

    await waitFor(() =>
      expect(upsertCompanyDocumentParam).toHaveBeenCalledWith(
        TENANT,
        { documentTypeCode: "soat", state: "OCULTO" },
        undefined,
      ),
    );
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("agrega un parámetro nuevo (sin catálogo: código a mano)", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([]);
    vi.mocked(upsertCompanyDocumentParam).mockResolvedValue(param("cepd", "OPCIONAL"));
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    await screen.findByText(/se aplica el comportamiento base/i);
    const [open] = screen.getAllByRole("button", { name: /agregar parámetro/i });
    await userEvent.click(open);
    const dialog = screen.getByRole("dialog");
    await userEvent.type(within(dialog).getByLabelText("Código de documento"), "cepd");
    await userEvent.click(within(dialog).getByRole("radio", { name: /opcional/i }));
    await userEvent.click(within(dialog).getByRole("button", { name: "Guardar" }));

    await waitFor(() =>
      expect(upsertCompanyDocumentParam).toHaveBeenCalledWith(
        TENANT,
        { documentTypeCode: "cepd", state: "OPCIONAL" },
        undefined,
      ),
    );
    expect(await screen.findByText("cepd")).toBeInTheDocument();
  });

  it("con catálogo, ofrece un selector de documentos y guarda su código", async () => {
    vi.mocked(fetchDocumentTypes).mockResolvedValue({
      data: [
        { id: "d1", codigo: "licencia", nombre: "Licencia de tránsito", estado: "activo", fechaCreacion: "2026-01-01" },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 100,
    });
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([]);
    vi.mocked(upsertCompanyDocumentParam).mockResolvedValue(param("licencia", "OBLIGATORIO"));
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    await screen.findByText(/se aplica el comportamiento base/i);
    await waitFor(() => expect(fetchDocumentTypes).toHaveBeenCalled());
    await userEvent.click(screen.getAllByRole("button", { name: /agregar parámetro/i })[0]);
    const dialog = screen.getByRole("dialog");
    await userEvent.selectOptions(await within(dialog).findByLabelText("Documento"), "licencia");
    await userEvent.click(within(dialog).getByRole("button", { name: "Guardar" }));

    await waitFor(() =>
      expect(upsertCompanyDocumentParam).toHaveBeenCalledWith(
        TENANT,
        { documentTypeCode: "licencia", state: "OBLIGATORIO" },
        undefined,
      ),
    );
  });

  it("muestra el error de guardado dentro del modal", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([param("soat", "OPCIONAL")]);
    vi.mocked(upsertCompanyDocumentParam).mockRejectedValue(new ApiError(403, "x"));
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    await userEvent.click(await screen.findByRole("button", { name: "Editar soat" }));
    await userEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Guardar" }));
    expect(await screen.findByText(/no tienes permiso para modificar/i)).toBeInTheDocument();
  });

  it("consulta por ruta de hijo cuando hay networkHeadId", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} networkHeadId="head-1" />);

    await waitFor(() =>
      expect(fetchCompanyDocumentParams).toHaveBeenCalledWith(TENANT, expect.anything(), "head-1"),
    );
  });
});
