// HU #14702 y #14703 (épica #14666): «Firmado por todas las partes» como filtro y como columna, y
// «Falta firmar» solo como columna. La consola es la misma para gestor, organismo y Super Admin.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type { QueryField } from "@/lib/api/queries";
import type { CompanyQueryResult, CompanyQueryRow } from "@/lib/api/company-queries";

const mocks = vi.hoisted(() => ({
  buildXlsx: vi.fn((..._args: unknown[]) => new Uint8Array()),
  download: vi.fn(),
  fetchCompanyQueryFields: vi.fn(),
  fetchCompanySavedQueries: vi.fn(),
  runCompanyQuery: vi.fn(),
  saveCompanyQuery: vi.fn(),
  deleteCompanySavedQuery: vi.fn(),
}));

vi.mock("@/lib/api/company-queries", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/company-queries")>();
  return { ...actual, ...mocks };
});

vi.mock("@/lib/xlsx", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/xlsx")>();
  return { ...actual, buildXlsx: mocks.buildXlsx };
});

vi.mock("@/components/consultas/export", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/components/consultas/export")>();
  return { ...actual, download: mocks.download };
});

import { ConsultasTab } from "@/components/atom/modules/_reportes/tabs/ConsultasTab";
import { COMPANY_QUERY_COLUMNS } from "@/components/atom/modules/_reportes/consultas/company-columns";

const AYUDA_FILTRO = "Sí: todas las partes del trámite ya firmaron. No: falta firmar al menos una.";
const AYUDA_FALTA = "Nombre y rol de quien aún no ha firmado. Vacío si ya firmaron todos.";

const FIELDS: QueryField[] = [
  {
    id: "firmado_por_todas",
    label: "Firmado por todas las partes",
    kind: "booleano",
    group: "Personas",
    operators: ["es_alguno"],
    options: [
      { value: "true", label: "Sí" },
      { value: "false", label: "No" },
    ],
    hint: AYUDA_FILTRO,
    admiteLista: false,
  },
  {
    id: "falta_firmar",
    label: "Falta firmar",
    kind: "texto",
    group: "Personas",
    operators: [],
    options: [],
    hint: AYUDA_FALTA,
    admiteLista: false,
  },
];

function row(overrides: Partial<CompanyQueryRow> = {}): CompanyQueryRow {
  return {
    procedureInstanceId: "p1",
    referenceNumber: "REF-1",
    placa: "ABC123",
    vin: "VIN0001",
    transitOfficeId: "o1",
    transitOfficeName: "Secretaría de Movilidad",
    companiaId: "empresa-1",
    companiaNombre: "Mi Empresa",
    procedureTypeId: "t1",
    procedureTypeName: "Traspaso de vehículo",
    status: "entregado",
    prioritario: false,
    subsanacionActiva: false,
    subsanacionCount: 0,
    comprador: "Cándida Compradora",
    vendedor: "Vera Vendedora",
    tienePrenda: false,
    acreedorPrenda: null,
    tieneLicenciaTransito: true,
    transformaciones: [],
    esLeasing: false,
    metodoPago: "Efectivo",
    tipoTraspaso: "bilateral",
    radicadoPor: "Gustavo Gestor",
    creadoEn: "2026-08-01T14:00:00Z",
    enviadoEn: null,
    cerradoEn: null,
    aprobadoEn: null,
    actualizadoEn: null,
    diasHastaEnvio: null,
    diasEnOrganismo: null,
    devoluciones: 0,
    ...overrides,
  };
}

function result(filas: CompanyQueryRow[]): CompanyQueryResult {
  return {
    total: filas.length,
    page: 1,
    pageSize: 25,
    desde: "2026-07-07",
    hasta: "2026-08-05",
    totalPeriodoAnterior: 0,
    filas,
    cobertura: [],
  };
}

beforeEach(() => {
  window.history.replaceState(null, "", "/");
  window.localStorage.clear();
  vi.clearAllMocks();
  mocks.fetchCompanyQueryFields.mockResolvedValue(FIELDS);
  mocks.fetchCompanySavedQueries.mockResolvedValue([]);
  mocks.runCompanyQuery.mockResolvedValue(result([row()]));
});

async function abrirColumnas() {
  await screen.findByTestId("empresa-query-resultado");
  await userEvent.click(screen.getByTestId("empresa-query-column-picker"));
  return screen.getByTestId("empresa-query-column-picker-panel");
}

describe("HU #14702 — filtro «Firmado por todas las partes»", () => {
  it("aparece en el grupo Personas con su ayuda, y «Falta firmar» no se ofrece como filtro", async () => {
    render(<ConsultasTab />);
    await screen.findByTestId("empresa-query-resultado");

    await userEvent.click(screen.getByTestId("empresa-query-agregar-filtro"));
    const campos = screen.getByTestId("empresa-query-campos");

    expect(within(campos).getByText("Personas")).toBeInTheDocument();
    expect(within(campos).getByRole("button", { name: "Firmado por todas las partes" })).toBeInTheDocument();
    expect(within(campos).queryByText("Falta firmar")).not.toBeInTheDocument();

    await userEvent.click(within(campos).getByRole("button", { name: "Firmado por todas las partes" }));
    expect(screen.getByTestId("empresa-query-editor-firmado_por_todas")).toHaveTextContent(AYUDA_FILTRO);
  });

  it("el único operador es «es» con valores Sí y No, y al aplicarlo cambian la consulta y el contador", async () => {
    mocks.runCompanyQuery
      .mockResolvedValueOnce(result([row(), row({ procedureInstanceId: "p2", referenceNumber: "REF-2" })]))
      .mockResolvedValue(result([row({ firmadoPorTodas: true })]));

    render(<ConsultasTab />);
    await screen.findByTestId("empresa-query-resultado");
    await waitFor(() => expect(screen.getByTestId("empresa-query-total")).toHaveTextContent("2"));

    await userEvent.click(screen.getByTestId("empresa-query-agregar-filtro"));
    await userEvent.click(screen.getByRole("button", { name: "Firmado por todas las partes" }));

    const editor = screen.getByTestId("empresa-query-editor-firmado_por_todas");
    // Un solo operador: no se pinta el selector.
    expect(within(editor).queryByLabelText("Operador")).not.toBeInTheDocument();
    expect(within(editor).getByRole("checkbox", { name: "Sí" })).toBeInTheDocument();
    expect(within(editor).getByRole("checkbox", { name: "No" })).toBeInTheDocument();

    await userEvent.click(within(editor).getByRole("checkbox", { name: "Sí" }));
    await userEvent.click(screen.getByTestId("empresa-query-aplicar-firmado_por_todas"));

    await waitFor(() =>
      expect(mocks.runCompanyQuery).toHaveBeenLastCalledWith(
        expect.objectContaining({
          condiciones: [{ fieldId: "firmado_por_todas", operator: "es_alguno", values: ["true"] }],
        }),
        expect.anything(),
      ),
    );
    await waitFor(() => expect(screen.getByTestId("empresa-query-total")).toHaveTextContent("1"));
    expect(screen.getByTestId("empresa-query-chip-firmado_por_todas")).toHaveTextContent(
      "Firmado por todas las partes",
    );
  });
});

describe("HU #14703 — columnas «Firmado por todas las partes» y «Falta firmar»", () => {
  it("se muestran y se ocultan como las demás columnas", async () => {
    render(<ConsultasTab />);
    const panel = await abrirColumnas();

    const firmado = within(panel).getByRole("checkbox", { name: "Firmado por todas las partes" });
    const falta = within(panel).getByRole("checkbox", { name: "Falta firmar" });
    expect(firmado).not.toBeChecked();

    await userEvent.click(firmado);
    await userEvent.click(falta);
    expect(screen.getByRole("columnheader", { name: "Firmado por todas las partes" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Falta firmar" })).toBeInTheDocument();

    await userEvent.click(firmado);
    await userEvent.click(falta);
    expect(screen.queryByRole("columnheader", { name: "Firmado por todas las partes" })).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Falta firmar" })).not.toBeInTheDocument();
  });

  it("pinta Sí/No, quién falta y celda vacía si todos firmaron", async () => {
    mocks.runCompanyQuery.mockResolvedValue(
      result([
        row({ procedureInstanceId: "a", firmadoPorTodas: false, faltaFirmar: "Vendedor: Vera Vendedora" }),
        row({ procedureInstanceId: "b", firmadoPorTodas: true, faltaFirmar: "" }),
      ]),
    );
    render(<ConsultasTab />);
    const panel = await abrirColumnas();
    await userEvent.click(within(panel).getByRole("checkbox", { name: "Firmado por todas las partes" }));
    await userEvent.click(within(panel).getByRole("checkbox", { name: "Falta firmar" }));

    const celdas = await screen.findAllByTestId("empresa-query-celda-falta_firmar");
    expect(celdas[0]).toHaveTextContent("Vendedor: Vera Vendedora");
    expect(celdas[1].textContent).toBe("");

    const filas = screen.getAllByRole("row").slice(1);
    expect(within(filas[0]).getByText("No")).toBeInTheDocument();
    expect(within(filas[1]).getByText("Sí")).toBeInTheDocument();
  });

  it("un texto largo se parte en líneas dentro de la celda sin ensanchar la tabla", async () => {
    const largo = Array.from({ length: 8 }, (_, i) => `Vendedor ${i + 1}: Persona con nombre muy largo ${i}`).join(
      " · ",
    );
    mocks.runCompanyQuery.mockResolvedValue(result([row({ firmadoPorTodas: false, faltaFirmar: largo })]));
    render(<ConsultasTab />);
    const panel = await abrirColumnas();
    await userEvent.click(within(panel).getByRole("checkbox", { name: "Falta firmar" }));

    const celda = await screen.findByTestId("empresa-query-celda-falta_firmar");
    expect(celda).toHaveTextContent(largo);
    expect(celda.className).toContain("max-w-[22rem]");
    expect(celda.className).toContain("break-words");
  });

  it("en una consulta que no lo calculó muestra «—», no un «No» que afirmaría que falta alguien", () => {
    const firmado = COMPANY_QUERY_COLUMNS.find((c) => c.id === "firmado_por_todas")!;
    const falta = COMPANY_QUERY_COLUMNS.find((c) => c.id === "falta_firmar")!;
    expect(firmado.value(row())).toBe("—");
    expect(firmado.value(row({ firmadoPorTodas: null }))).toBe("—");
    expect(falta.value(row())).toBe("—");
    expect(firmado.raw!(row({ firmadoPorTodas: true }))).toBe("Sí");
    expect(falta.raw!(row({ faltaFirmar: "" }))).toBeNull();
    // El Excel dice lo mismo que la tabla: «—» cuando no se calculó, no una celda vacía.
    expect(firmado.raw!(row())).toBe("—");
    expect(falta.raw!(row())).toBe("—");
  });
});

async function filtrarFirmadoNo() {
  await userEvent.click(screen.getByTestId("empresa-query-agregar-filtro"));
  await userEvent.click(screen.getByRole("button", { name: "Firmado por todas las partes" }));
  const editor = screen.getByTestId("empresa-query-editor-firmado_por_todas");
  await userEvent.click(within(editor).getByRole("checkbox", { name: "No" }));
  await userEvent.click(screen.getByTestId("empresa-query-aplicar-firmado_por_todas"));
}

const FILTRO_NO = [{ fieldId: "firmado_por_todas", operator: "es_alguno", values: ["false"] }];

describe("HU #14704 — guardar, reabrir y exportar con los campos de firma", () => {
  it("guarda y reabre conservando el filtro, el valor y las columnas visibles", async () => {
    mocks.saveCompanyQuery.mockImplementation(async (input) => ({
      id: "q1",
      nombre: input.nombre,
      descripcion: null,
      deFabrica: false,
      definition: input.definition,
      createdAt: "2026-08-01T00:00:00Z",
      updatedAt: null,
    }));
    render(<ConsultasTab />);
    const panel = await abrirColumnas();
    await userEvent.click(within(panel).getByRole("checkbox", { name: "Firmado por todas las partes" }));
    await userEvent.click(within(panel).getByRole("checkbox", { name: "Falta firmar" }));
    await filtrarFirmadoNo();

    await userEvent.click(screen.getByRole("button", { name: "Guardar consulta" }));
    await userEvent.type(screen.getByTestId("empresa-query-nombre-input"), "Pendientes de firma");
    await userEvent.click(screen.getByTestId("empresa-query-guardar-confirmar"));

    await waitFor(() => expect(mocks.saveCompanyQuery).toHaveBeenCalled());
    const enviada = mocks.saveCompanyQuery.mock.calls[0][0].definition;
    expect(enviada.condiciones).toEqual(FILTRO_NO);
    expect(enviada.columnas).toEqual(expect.arrayContaining(["firmado_por_todas", "falta_firmar"]));

    // Cierra (empezar de cero) y reabre la guardada desde la lista.
    await userEvent.click(screen.getByTestId("empresa-query-nueva"));
    await waitFor(() =>
      expect(screen.queryByTestId("empresa-query-chip-firmado_por_todas")).not.toBeInTheDocument(),
    );
    await userEvent.click(screen.getByTestId("empresa-query-guardada-q1"));

    const chip = await screen.findByTestId("empresa-query-chip-firmado_por_todas");
    expect(chip).toHaveTextContent("Firmado por todas las partes");
    expect(chip).toHaveTextContent("No");
    expect(screen.getByRole("columnheader", { name: "Firmado por todas las partes" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Falta firmar" })).toBeInTheDocument();
    await waitFor(() =>
      expect(mocks.runCompanyQuery).toHaveBeenLastCalledWith(
        expect.objectContaining({ condiciones: FILTRO_NO }),
        expect.anything(),
      ),
    );
  });

  it("una consulta guardada antes del cambio corre igual y admite agregar los campos nuevos", async () => {
    mocks.fetchCompanySavedQueries.mockResolvedValue([
      {
        id: "vieja",
        nombre: "Antigua",
        descripcion: null,
        deFabrica: false,
        definition: {
          fechas: { preset: "ultimos_30" },
          condiciones: [],
          columnas: ["placa", "comprador"],
        },
        createdAt: "2026-01-01T00:00:00Z",
        updatedAt: null,
      },
    ]);
    render(<ConsultasTab />);
    await userEvent.click(await screen.findByTestId("empresa-query-guardada-vieja"));
    await waitFor(() => expect(screen.getByRole("columnheader", { name: "Placa" })).toBeInTheDocument());

    expect(screen.queryByRole("columnheader", { name: "Firmado por todas las partes" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByTestId("empresa-query-column-picker"));
    await userEvent.click(
      within(screen.getByTestId("empresa-query-column-picker-panel")).getByRole("checkbox", { name: "Falta firmar" }),
    );
    expect(screen.getByRole("columnheader", { name: "Falta firmar" })).toBeInTheDocument();
  });

  it("exporta a Excel las columnas visibles con los valores de la tabla y solo las filas filtradas", async () => {
    const filtradas = [
      row({ procedureInstanceId: "a", firmadoPorTodas: false, faltaFirmar: "Vendedor: Vera Vendedora" }),
      row({ procedureInstanceId: "b", referenceNumber: "REF-2", firmadoPorTodas: false, faltaFirmar: "Comprador: Cándida" }),
    ];
    mocks.runCompanyQuery.mockResolvedValueOnce(result([row({ firmadoPorTodas: true }), ...filtradas]));
    mocks.runCompanyQuery.mockResolvedValue(result(filtradas));
    render(<ConsultasTab />);
    const panel = await abrirColumnas();
    await userEvent.click(within(panel).getByRole("checkbox", { name: "Firmado por todas las partes" }));
    await userEvent.click(within(panel).getByRole("checkbox", { name: "Falta firmar" }));
    await filtrarFirmadoNo();
    await waitFor(() => expect(screen.getByTestId("empresa-query-total")).toHaveTextContent("2"));

    await userEvent.click(screen.getByTestId("empresa-query-export-xlsx"));
    await waitFor(() => expect(mocks.download).toHaveBeenCalled());

    // Las filas que se piden para exportar llevan el mismo filtro que la pantalla.
    expect(mocks.runCompanyQuery).toHaveBeenLastCalledWith(
      expect.objectContaining({ condiciones: FILTRO_NO }),
      expect.anything(),
    );
    const libro = mocks.buildXlsx.mock.calls.at(-1)![0] as {
      columns: { header: string }[];
      rows: unknown[][];
    };
    const iFirmado = libro.columns.findIndex((c) => c.header === "Firmado por todas las partes");
    const iFalta = libro.columns.findIndex((c) => c.header === "Falta firmar");
    expect(iFirmado).toBeGreaterThanOrEqual(0);
    expect(iFalta).toBeGreaterThanOrEqual(0);
    expect(libro.rows.map((r) => [r[iFirmado], r[iFalta]])).toEqual([
      ["No", "Vendedor: Vera Vendedora"],
      ["No", "Comprador: Cándida"],
    ]);
  });

  it("con las columnas ocultas el Excel no las incluye", async () => {
    mocks.runCompanyQuery.mockResolvedValue(result([row({ firmadoPorTodas: false, faltaFirmar: "X" })]));
    render(<ConsultasTab />);
    await screen.findByTestId("empresa-query-resultado");
    await waitFor(() => expect(screen.getByTestId("empresa-query-total")).toHaveTextContent("1"));
    await userEvent.click(screen.getByTestId("empresa-query-export-xlsx"));
    await waitFor(() => expect(mocks.download).toHaveBeenCalled());
    const libro = mocks.buildXlsx.mock.calls.at(-1)![0] as { columns: { header: string }[] };
    const headers = libro.columns.map((c) => c.header);
    expect(headers).not.toContain("Firmado por todas las partes");
    expect(headers).not.toContain("Falta firmar");
  });

  it("si el servidor falla al exportar muestra el error y no pierde la consulta en pantalla", async () => {
    render(<ConsultasTab />);
    await screen.findByTestId("empresa-query-resultado");
    await filtrarFirmadoNo();
    await screen.findByTestId("empresa-query-chip-firmado_por_todas");
    await waitFor(() => expect(screen.getByTestId("empresa-query-export-xlsx")).toBeEnabled());
    mocks.runCompanyQuery.mockRejectedValue(new Error("503"));
    await userEvent.click(screen.getByTestId("empresa-query-export-xlsx"));

    expect(await screen.findByText(/No se pudo exportar: 503/)).toBeInTheDocument();
    expect(screen.getByTestId("empresa-query-chip-firmado_por_todas")).toBeInTheDocument();
    expect(mocks.download).not.toHaveBeenCalled();
  });
});

describe("Catálogo que no llega", () => {
  it("no ofrece filtros (ni un filtro vacío) y la tabla sigue sirviendo", async () => {
    mocks.fetchCompanyQueryFields.mockRejectedValue(new Error("503"));
    render(<ConsultasTab />);
    await screen.findByTestId("empresa-query-resultado");

    expect(screen.getByTestId("empresa-query-agregar-filtro")).toBeDisabled();
  });
});
