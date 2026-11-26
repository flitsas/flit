// HU #14702 y #14703 (épica #14666, HU #14696): «Firmas» como filtro (Completas / Faltan firmas) y como
// columna con «N de M» y una línea por persona; «Falta firmar» ya no se ofrece.
// La consola es la misma para gestor, organismo y Super Admin.
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

const AYUDA_FILTRO =
  "Completas: todas las partes que deben firmar ya firmaron. Faltan firmas: falta al menos una.";
const AYUDA_FALTA = "Nombre y rol de quien aún no ha firmado. Vacío si ya firmaron todos.";

const FIELDS: QueryField[] = [
  {
    id: "firmado_por_todas",
    label: "Firmas",
    kind: "booleano",
    group: "Personas",
    operators: ["es_alguno"],
    options: [
      { value: "true", label: "Completas" },
      { value: "false", label: "Faltan firmas" },
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
    expect(within(campos).getByRole("button", { name: "Firmas" })).toBeInTheDocument();
    expect(within(campos).queryByText("Falta firmar")).not.toBeInTheDocument();

    await userEvent.click(within(campos).getByRole("button", { name: "Firmas" }));
    expect(screen.getByTestId("empresa-query-editor-firmado_por_todas")).toHaveTextContent(AYUDA_FILTRO);
  });

  it("el único operador es «es» con valores Completas y Faltan firmas, y al aplicarlo cambian la consulta y el contador", async () => {
    mocks.runCompanyQuery
      .mockResolvedValueOnce(result([row(), row({ procedureInstanceId: "p2", referenceNumber: "REF-2" })]))
      .mockResolvedValue(result([row({ firmadoPorTodas: true })]));

    render(<ConsultasTab />);
    await screen.findByTestId("empresa-query-resultado");
    await waitFor(() => expect(screen.getByTestId("empresa-query-total")).toHaveTextContent("2"));

    await userEvent.click(screen.getByTestId("empresa-query-agregar-filtro"));
    await userEvent.click(screen.getByRole("button", { name: "Firmas" }));

    const editor = screen.getByTestId("empresa-query-editor-firmado_por_todas");
    // Un solo operador: no se pinta el selector.
    expect(within(editor).queryByLabelText("Operador")).not.toBeInTheDocument();
    expect(within(editor).getByRole("checkbox", { name: "Completas" })).toBeInTheDocument();
    expect(within(editor).getByRole("checkbox", { name: "Faltan firmas" })).toBeInTheDocument();
    expect(within(editor).queryByRole("checkbox", { name: "Sí" })).not.toBeInTheDocument();

    await userEvent.click(within(editor).getByRole("checkbox", { name: "Completas" }));
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
      "Firmas",
    );
  });
});

const PERSONAS = [
  { rol: "Vendedor", ordinal: 1, nombre: "Vera Vendedora", firmo: true, estado: "Firmó" },
  { rol: "Comprador", ordinal: 1, nombre: "Cándida Compradora", firmo: false, estado: "Pendiente" },
  { rol: "Comprador", ordinal: 2, nombre: "Camilo Comprador", firmo: false, estado: "Rechazada" },
  { rol: "Comprador", ordinal: 3, nombre: "Clara Compradora", firmo: false, estado: "Sin validar" },
];

describe("HU #14703 / #14696 — columna «Firmas»", () => {
  it("se muestra y se oculta como las demás columnas; «Falta firmar» ya no es una columna", async () => {
    render(<ConsultasTab />);
    const panel = await abrirColumnas();

    const firmas = within(panel).getByRole("checkbox", { name: "Firmas" });
    expect(within(panel).queryByRole("checkbox", { name: "Falta firmar" })).not.toBeInTheDocument();
    expect(within(panel).getByRole("checkbox", { name: "Firma vendedor" })).toBeInTheDocument();
    expect(within(panel).getByRole("checkbox", { name: "Firma comprador" })).toBeInTheDocument();
    expect(firmas).not.toBeChecked();

    await userEvent.click(firmas);
    expect(await screen.findByRole("columnheader", { name: "Firmas" })).toBeInTheDocument();

    await userEvent.click(firmas);
    await waitFor(() =>
      expect(screen.queryByRole("columnheader", { name: "Firmas" })).not.toBeInTheDocument(),
    );
  });

  it("pinta «N de M» y una línea por persona con su rol y el chip de su estado", async () => {
    mocks.runCompanyQuery.mockResolvedValue(
      result([
        row({
          procedureInstanceId: "a",
          firmadoPorTodas: false,
          firmasResumen: "1 de 4 · Faltan firmas",
          firmasPersonas: PERSONAS,
        }),
        row({ procedureInstanceId: "b", firmadoPorTodas: true, firmasResumen: "Firmas completas", firmasPersonas: [] }),
      ]),
    );
    render(<ConsultasTab />);
    const panel = await abrirColumnas();
    await userEvent.click(within(panel).getByRole("checkbox", { name: "Firmas" }));

    const celdas = await screen.findAllByTestId("firmas-celda");
    expect(within(celdas[0]).getByTestId("firmas-resumen")).toHaveTextContent("1 de 4 · Faltan firmas");

    const lineas = within(celdas[0]).getAllByTestId("firma-persona");
    expect(lineas).toHaveLength(4);
    // Vendedores primero, con su rol y su estado.
    expect(lineas[0]).toHaveTextContent("Vera Vendedora");
    expect(lineas[0]).toHaveTextContent("(Vendedor)");
    expect(lineas[0]).toHaveTextContent("Firmó");
    expect(lineas[1]).toHaveTextContent("Cándida Compradora");
    expect(lineas[1]).toHaveTextContent("(Comprador)");
    expect(lineas[1]).toHaveTextContent("Pendiente");
    expect(lineas[2]).toHaveTextContent("Rechazada");
    expect(lineas[3]).toHaveTextContent("Sin validar");

    // El texto suelto de «Falta firmar» ya no aparece.
    expect(celdas[0]).not.toHaveTextContent("Vendedor: Vera");
    // Sin personas que firmen: solo el resumen.
    expect(within(celdas[1]).getByTestId("firmas-resumen")).toHaveTextContent("Firmas completas");
    expect(within(celdas[1]).queryAllByTestId("firma-persona")).toHaveLength(0);
  });

  it("sin firmasPersonas usa los actores y, si un actor no trae estado, dice «Sin validar»", async () => {
    mocks.runCompanyQuery.mockResolvedValue(
      result([
        row({
          firmasResumen: "0 de 2 · Faltan firmas",
          actores: [
            {
              rol: "vendedor", ordinal: 1, nombre: "Vera Vendedora", tipoDocumento: "CC", documento: "1",
              estadoValidacion: "aprobado", motivoRechazo: null, intentos: 1, estadoFirma: "Pendiente",
            },
            {
              rol: "comprador", ordinal: 1, nombre: "Cándida Compradora", tipoDocumento: "CC", documento: "2",
              estadoValidacion: "sin_validacion", motivoRechazo: null, intentos: 0,
            },
          ],
        }),
      ]),
    );
    render(<ConsultasTab />);
    const panel = await abrirColumnas();
    await userEvent.click(within(panel).getByRole("checkbox", { name: "Firmas" }));

    const lineas = await screen.findAllByTestId("firma-persona");
    expect(lineas[0]).toHaveTextContent("Pendiente");
    expect(lineas[1]).toHaveTextContent("Sin validar");
  });

  it("en una consulta que no calculó la firma muestra «—», no un «faltan» que afirmaría algo", () => {
    const firmas = COMPANY_QUERY_COLUMNS.find((c) => c.id === "firmado_por_todas")!;
    expect(firmas.label).toBe("Firmas");
    expect(firmas.value(row())).toBe("—");
    expect(firmas.value(row({ firmadoPorTodas: null }))).toBe("—");
    expect(firmas.raw!(row())).toBe("—");
    expect(firmas.value(row({ firmasResumen: "3 de 3 · Firmas completas" }))).toBe("3 de 3 · Firmas completas");
    expect(COMPANY_QUERY_COLUMNS.find((c) => c.id === "falta_firmar")).toBeUndefined();
  });
});

async function filtrarFirmadoNo() {
  await userEvent.click(screen.getByTestId("empresa-query-agregar-filtro"));
  await userEvent.click(screen.getByRole("button", { name: "Firmas" }));
  const editor = screen.getByTestId("empresa-query-editor-firmado_por_todas");
  await userEvent.click(within(editor).getByRole("checkbox", { name: "Faltan firmas" }));
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
    await userEvent.click(within(panel).getByRole("checkbox", { name: "Firmas" }));
    await filtrarFirmadoNo();

    await userEvent.click(screen.getByRole("button", { name: "Guardar consulta" }));
    await userEvent.type(screen.getByTestId("empresa-query-nombre-input"), "Pendientes de firma");
    await userEvent.click(screen.getByTestId("empresa-query-guardar-confirmar"));

    await waitFor(() => expect(mocks.saveCompanyQuery).toHaveBeenCalled());
    const enviada = mocks.saveCompanyQuery.mock.calls[0][0].definition;
    expect(enviada.condiciones).toEqual(FILTRO_NO);
    expect(enviada.columnas).toEqual(expect.arrayContaining(["firmado_por_todas"]));

    // Cierra (empezar de cero) y reabre la guardada desde la lista.
    await userEvent.click(screen.getByTestId("empresa-query-nueva"));
    await waitFor(() =>
      expect(screen.queryByTestId("empresa-query-chip-firmado_por_todas")).not.toBeInTheDocument(),
    );
    await userEvent.click(screen.getByTestId("empresa-query-guardada-q1"));

    const chip = await screen.findByTestId("empresa-query-chip-firmado_por_todas");
    expect(chip).toHaveTextContent("Firmas");
    expect(chip).toHaveTextContent("Faltan firmas");
    expect(screen.getByRole("columnheader", { name: "Firmas" })).toBeInTheDocument();
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

    expect(screen.queryByRole("columnheader", { name: "Firmas" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByTestId("empresa-query-column-picker"));
    await userEvent.click(
      within(screen.getByTestId("empresa-query-column-picker-panel")).getByRole("checkbox", { name: "Firmas" }),
    );
    expect(screen.getByRole("columnheader", { name: "Firmas" })).toBeInTheDocument();
  });

  it("exporta a Excel las columnas visibles con los valores de la tabla y solo las filas filtradas", async () => {
    const filtradas = [
      row({
        procedureInstanceId: "a",
        firmadoPorTodas: false,
        firmasResumen: "1 de 2 · Faltan firmas",
        firmasPersonas: [
          { rol: "Vendedor", ordinal: 1, nombre: "Vera", firmo: true, estado: "Firmó" },
          { rol: "Comprador", ordinal: 1, nombre: "Cándida", firmo: false, estado: "Pendiente" },
        ],
      }),
      row({
        procedureInstanceId: "b",
        referenceNumber: "REF-2",
        firmadoPorTodas: false,
        firmasResumen: "0 de 1 · Faltan firmas",
        firmasPersonas: [{ rol: "Comprador", ordinal: 1, nombre: "Cándida", firmo: false, estado: "Rechazada" }],
      }),
    ];
    mocks.runCompanyQuery.mockResolvedValueOnce(result([row({ firmadoPorTodas: true }), ...filtradas]));
    mocks.runCompanyQuery.mockResolvedValue(result(filtradas));
    render(<ConsultasTab />);
    const panel = await abrirColumnas();
    await userEvent.click(within(panel).getByRole("checkbox", { name: "Firmas" }));
    await userEvent.click(within(panel).getByRole("checkbox", { name: "Firma vendedor" }));
    await userEvent.click(within(panel).getByRole("checkbox", { name: "Firma comprador" }));
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
    const iFirmado = libro.columns.findIndex((c) => c.header === "Firmas");
    const iVend = libro.columns.findIndex((c) => c.header === "Firma vendedor");
    const iComp = libro.columns.findIndex((c) => c.header === "Firma comprador");
    expect(iFirmado).toBeGreaterThanOrEqual(0);
    expect(iVend).toBeGreaterThanOrEqual(0);
    expect(iComp).toBeGreaterThanOrEqual(0);
    expect(libro.columns.map((c) => c.header)).not.toContain("Falta firmar");
    // Mismo criterio que la tabla: «N de M» y el estado de cada persona.
    expect(libro.rows.map((r) => [r[iFirmado], r[iVend], r[iComp]])).toEqual([
      ["1 de 2 · Faltan firmas", "Firmó", "Pendiente"],
      ["0 de 1 · Faltan firmas", null, "Rechazada"],
    ]);
  });

  it("con las columnas ocultas el Excel no las incluye", async () => {
    mocks.runCompanyQuery.mockResolvedValue(result([row({ firmadoPorTodas: false, firmasResumen: "0 de 1 · Faltan firmas" })]));
    render(<ConsultasTab />);
    await screen.findByTestId("empresa-query-resultado");
    await waitFor(() => expect(screen.getByTestId("empresa-query-total")).toHaveTextContent("1"));
    await userEvent.click(screen.getByTestId("empresa-query-export-xlsx"));
    await waitFor(() => expect(mocks.download).toHaveBeenCalled());
    const libro = mocks.buildXlsx.mock.calls.at(-1)![0] as { columns: { header: string }[] };
    const headers = libro.columns.map((c) => c.header);
    expect(headers).not.toContain("Firmas");
    expect(headers).not.toContain("Firma vendedor");
    expect(headers).not.toContain("Firma comprador");
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
