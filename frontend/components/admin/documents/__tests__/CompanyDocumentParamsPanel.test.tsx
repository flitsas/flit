// HU #10523 (RF31) + HU #14000 — panel de parámetros documentales por gestora con TODOS los
// documentos del catálogo y filtros por columna. API mockeada.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyDocumentParamsPanel } from "../CompanyDocumentParamsPanel";
import type { CompanyDocumentParam } from "@/lib/api/admin-company-document-params";
import type { DocumentType } from "@/lib/api/types-documents";
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

function doc(
  codigo: string,
  nombre: string,
  estado: DocumentType["estado"] = "activo",
): DocumentType {
  return { id: `d-${codigo}`, codigo, nombre, estado, fechaCreacion: "2026-01-01" };
}

function mockCatalog(data: DocumentType[]) {
  vi.mocked(fetchDocumentTypes).mockResolvedValue({
    data,
    totalCount: data.length,
    page: 1,
    pageSize: 100,
  });
}

const BASIC_CATALOG = [
  doc("soat", "SOAT"),
  doc("cepd", "Certificado de paz y salvo"),
  doc("licencia", "Licencia de tránsito"),
];

async function applyFilter(column: string, fill: (dialog: HTMLElement) => Promise<void>) {
  await userEvent.click(screen.getByRole("button", { name: /\+ filtro/i }));
  const panel = screen.getByRole("dialog", { name: /filtros de la tabla/i });
  await userEvent.click(within(panel).getByRole("button", { name: column }));
  await fill(panel);
  await userEvent.click(within(panel).getByRole("button", { name: "Aplicar" }));
}

describe("CompanyDocumentParamsPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockCatalog(BASIC_CATALOG);
  });

  it("muestra el estado de carga", () => {
    vi.mocked(fetchCompanyDocumentParams).mockReturnValue(new Promise(() => {}));
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);
    expect(screen.getByText(/cargando parámetros documentales/i)).toBeInTheDocument();
  });

  it("AC1: lista todos los documentos activos del catálogo; los parametrizados con su estado y el resto «Base»", async () => {
    const catalog = Array.from({ length: 40 }, (_, i) =>
      doc(`doc${String(i).padStart(2, "0")}`, `Documento ${String(i).padStart(2, "0")}`),
    );
    mockCatalog(catalog);
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([
      param("doc00", "OBLIGATORIO"),
      param("doc01", "OPCIONAL"),
      param("doc02", "OCULTO"),
    ]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    expect(await screen.findByText("doc00")).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText(/filas por página/i), "50");
    expect(await screen.findByText("doc39")).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: /^Editar / })).toHaveLength(40);
    expect(screen.getByLabelText("Estado de doc00: Obligatorio")).toBeInTheDocument();
    expect(screen.getByLabelText("Estado de doc01: Opcional")).toBeInTheDocument();
    expect(screen.getByLabelText("Estado de doc02: Oculto")).toBeInTheDocument();
    expect(screen.getAllByLabelText(/: Base$/)).toHaveLength(37);
  }, 30000);

  it("trae todas las páginas del catálogo, no solo la primera", async () => {
    const page1 = Array.from({ length: 100 }, (_, i) => doc(`a${i}`, `A ${i}`));
    const page2 = [doc("zeta", "Zeta final")];
    vi.mocked(fetchDocumentTypes)
      .mockResolvedValueOnce({ data: page1, totalCount: 101, page: 1, pageSize: 100 })
      .mockResolvedValueOnce({ data: page2, totalCount: 101, page: 2, pageSize: 100 });
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    await screen.findByText("a0");
    expect(fetchDocumentTypes).toHaveBeenCalledTimes(2);
    expect(fetchDocumentTypes).toHaveBeenLastCalledWith(
      { page: 2, pageSize: 100 },
      expect.anything(),
    );
  });

  it("ignora los inactivos del catálogo pero no pierde un parámetro cuyo código ya no está activo", async () => {
    mockCatalog([doc("soat", "SOAT"), doc("viejo", "Documento viejo", "inactivo")]);
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([
      param("viejo", "OCULTO"),
      param("huerfano", "OPCIONAL"),
    ]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    expect(await screen.findByText("soat")).toBeInTheDocument();
    expect(screen.getByLabelText("Estado de viejo: Oculto")).toBeInTheDocument();
    expect(screen.getByLabelText("Estado de huerfano: Opcional")).toBeInTheDocument();
    expect(screen.getByText("Documento viejo")).toBeInTheDocument();
  });

  it("AC2: filtra por Documento (nombre o código)", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);
    await screen.findByText("soat");

    await applyFilter("Documento", async (p) => {
      await userEvent.type(within(p).getByLabelText(/documento contiene/i), "paz");
    });
    expect(screen.getByText("cepd")).toBeInTheDocument();
    expect(screen.queryByText("soat")).not.toBeInTheDocument();
    expect(screen.queryByText("licencia")).not.toBeInTheDocument();

    // Por código
    await userEvent.click(screen.getByRole("button", { name: /\+ filtro \(1\)/i }));
    await userEvent.click(screen.getByRole("button", { name: /Documento: «paz»/ }));
    const input = screen.getByLabelText(/documento contiene/i);
    await userEvent.clear(input);
    await userEvent.type(input, "lice");
    await userEvent.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(screen.getByText("licencia")).toBeInTheDocument();
    expect(screen.queryByText("cepd")).not.toBeInTheDocument();
  });

  it("AC2: filtra por Estado y combina con Documento (todas las condiciones)", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([
      param("soat", "OBLIGATORIO"),
      param("cepd", "OBLIGATORIO"),
    ]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);
    await screen.findByText("soat");

    await applyFilter("Estado", async (p) => {
      await userEvent.click(within(p).getByRole("checkbox", { name: "Obligatorio" }));
    });
    expect(screen.getByText("soat")).toBeInTheDocument();
    expect(screen.getByText("cepd")).toBeInTheDocument();
    expect(screen.queryByText("licencia")).not.toBeInTheDocument();

    await applyFilter("Documento", async (p) => {
      await userEvent.type(within(p).getByLabelText(/documento contiene/i), "soat");
    });
    expect(screen.getByText("soat")).toBeInTheDocument();
    expect(screen.queryByText("cepd")).not.toBeInTheDocument();
  });

  it("AC2: filtra por Estado «Base»", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([param("soat", "OBLIGATORIO")]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);
    await screen.findByText("soat");

    await applyFilter("Estado", async (p) => {
      await userEvent.click(within(p).getByRole("checkbox", { name: "Base" }));
    });
    expect(screen.queryByText("soat")).not.toBeInTheDocument();
    expect(screen.getByText("cepd")).toBeInTheDocument();
    expect(screen.getByText("licencia")).toBeInTheDocument();
  });

  it("AC3: editar un documento «Base» abre el modal con ese documento y, al guardar, la fila muestra el nuevo estado", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([]);
    vi.mocked(upsertCompanyDocumentParam).mockResolvedValue(param("licencia", "OPCIONAL"));
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    await userEvent.click(await screen.findByRole("button", { name: "Editar licencia" }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByLabelText("Código de documento")).toHaveValue("licencia");
    expect(within(dialog).getByText("Licencia de tránsito")).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole("radio", { name: /opcional/i }));
    await userEvent.click(within(dialog).getByRole("button", { name: "Guardar" }));

    await waitFor(() =>
      expect(upsertCompanyDocumentParam).toHaveBeenCalledWith(
        TENANT,
        { documentTypeCode: "licencia", state: "OPCIONAL" },
        undefined,
      ),
    );
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(screen.getByLabelText("Estado de licencia: Opcional")).toBeInTheDocument();
  });

  it("edita el estado de un documento ya parametrizado (upsert)", async () => {
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
    expect(await screen.findByLabelText("Estado de soat: Oculto")).toBeInTheDocument();
  });

  it("muestra el error de guardado dentro del modal", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([param("soat", "OPCIONAL")]);
    vi.mocked(upsertCompanyDocumentParam).mockRejectedValue(new ApiError(403, "x"));
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    await userEvent.click(await screen.findByRole("button", { name: "Editar soat" }));
    await userEvent.click(
      within(screen.getByRole("dialog")).getByRole("button", { name: "Guardar" }),
    );
    expect(await screen.findByText(/no tienes permiso para modificar/i)).toBeInTheDocument();
  });

  it("AC4: si el catálogo no carga, muestra el motivo con «Reintentar» y ninguna tabla parcial", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([param("soat", "OBLIGATORIO")]);
    vi.mocked(fetchDocumentTypes)
      .mockRejectedValueOnce(new ApiError(500, "x"))
      .mockResolvedValueOnce({ data: BASIC_CATALOG, totalCount: 3, page: 1, pageSize: 100 });
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    expect(
      await screen.findByText(/falló al cargar el catálogo de documentos \(error 500\)/i),
    ).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    expect(screen.queryByText("soat")).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByText("licencia")).toBeInTheDocument();
  });

  it("AC5: sin resultados muestra el vacío con «Limpiar filtros», que restablece la tabla", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);
    await screen.findByText("soat");

    await applyFilter("Documento", async (p) => {
      await userEvent.type(within(p).getByLabelText(/documento contiene/i), "zzzz");
    });
    expect(screen.getByText(/ningún registro coincide con los filtros/i)).toBeInTheDocument();
    expect(screen.queryByText("soat")).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Limpiar filtros" }));
    expect(await screen.findByText("soat")).toBeInTheDocument();
  });

  it("muestra el vacío cuando no hay documentos activos ni parámetros", async () => {
    mockCatalog([]);
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    expect(
      await screen.findByText(/no hay documentos activos en el catálogo/i),
    ).toBeInTheDocument();
  });

  it("muestra el motivo real del error de parámetros (403) y reintenta", async () => {
    vi.mocked(fetchCompanyDocumentParams)
      .mockRejectedValueOnce(
        new ApiError(403, "No se pudo completar la solicitud. Inténtalo de nuevo."),
      )
      .mockResolvedValueOnce([param("soat", "OPCIONAL")]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    expect(
      await screen.findByText(/no tienes permiso para ver los parámetros documentales/i),
    ).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByText("soat")).toBeInTheDocument();
    expect(fetchCompanyDocumentParams).toHaveBeenCalledTimes(2);
  });

  it("distingue un 500 y una respuesta con formato inesperado", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockRejectedValueOnce(new ApiError(500, "x"));
    const { unmount } = render(<CompanyDocumentParamsPanel tenantId={TENANT} />);
    expect(
      await screen.findByText(/falló al cargar los parámetros \(error 500\)/i),
    ).toBeInTheDocument();
    unmount();

    vi.mocked(fetchCompanyDocumentParams).mockResolvedValueOnce({} as never);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);
    expect(await screen.findByText(/no tiene el formato esperado/i)).toBeInTheDocument();
  });

  it("consulta por ruta de hijo cuando hay networkHeadId", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} networkHeadId="head-1" />);

    await waitFor(() =>
      expect(fetchCompanyDocumentParams).toHaveBeenCalledWith(TENANT, expect.anything(), "head-1"),
    );
  });
});
