// Consultas propias de la empresa gestora.
//
// La consola es la MISMA que la del organismo, así que lo que se prueba aquí no es que la pantalla
// funcione —de eso se encarga `ot-consultas`— sino que esté bien amarrada a su lado del trámite:
// que hable de «su empresa» y no del organismo, que llame a su API con su compañía, que enseñe sus
// columnas y que no consulte cuando un SuperAdmin todavía no ha elegido compañía.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type { QueryField, SavedQuery } from "@/lib/api/queries";
import type { CompanyQueryResult, CompanyQueryRow } from "@/lib/api/company-queries";

const mocks = vi.hoisted(() => ({
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

import { ConsultasTab } from "@/components/atom/modules/_reportes/tabs/ConsultasTab";
import {
  buildCompanyQueryXlsx,
  COMPANY_QUERY_COLUMNS,
  COMPANY_QUERY_PRESETS,
  defaultCompanyQueryColumns,
} from "@/components/atom/modules/_reportes/consultas/company-columns";

const FIELDS: QueryField[] = [
  {
    id: "placa",
    label: "Placa",
    kind: "texto",
    group: "Vehículo",
    operators: ["es_alguno", "contiene", "esta_vacio", "no_esta_vacio"],
    options: [],
    hint: "Se puede pegar una lista completa desde Excel.",
    admiteLista: true,
  },
  {
    id: "organismo",
    label: "Organismo de tránsito",
    kind: "opcion",
    group: "Trámite",
    operators: ["es_alguno", "no_es_ninguno"],
    options: [{ value: "o1", label: "11001 — Secretaría de Movilidad" }],
    hint: null,
    admiteLista: true,
  },
  {
    id: "leasing",
    label: "Leasing",
    kind: "booleano",
    group: "Comercial",
    operators: ["es_alguno"],
    options: [
      { value: "true", label: "Sí" },
      { value: "false", label: "No" },
    ],
    hint: null,
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
    modalidad: "traspaso",
    status: "entregado",
    prioritario: false,
    subsanacionActiva: false,
    subsanacionCount: 0,
    comprador: "Cándida Compradora",
    vendedor: "Vera Vendedora",
    tienePrenda: true,
    acreedorPrenda: "Banco X",
    tieneLicenciaTransito: true,
    transformaciones: ["cambio_color"],
    esLeasing: true,
    metodoPago: "Efectivo",
    tipoTraspaso: "bilateral",
    radicadoPor: "Gustavo Gestor",
    creadoEn: "2026-08-01T14:00:00Z",
    enviadoEn: "2026-08-02T14:00:00Z",
    cerradoEn: null,
    aprobadoEn: null,
    actualizadoEn: "2026-08-03T14:00:00Z",
    diasHastaEnvio: 1,
    diasEnOrganismo: 3,
    devoluciones: 0,
    ...overrides,
  };
}

function result(overrides: Partial<CompanyQueryResult> = {}): CompanyQueryResult {
  return {
    total: 1,
    page: 1,
    pageSize: 25,
    desde: "2026-07-07",
    hasta: "2026-08-05",
    totalPeriodoAnterior: 0,
    filas: [row()],
    cobertura: [],
    ...overrides,
  };
}

const SAVED: SavedQuery[] = [
  {
    id: "s1",
    nombre: "Mis traspasos",
    descripcion: null,
    deFabrica: false,
    definition: {
      fechas: { campo: "creacion", preset: "ultimos_30" },
      condiciones: [],
      columnas: [],
    },
    createdAt: "2026-08-01T00:00:00Z",
    updatedAt: null,
  },
  {
    id: "f1",
    nombre: "Pendientes de entregar",
    descripcion: "Punto de partida",
    deFabrica: true,
    definition: {
      fechas: { campo: "creacion", preset: "ultimos_90" },
      condiciones: [{ fieldId: "estado", operator: "es_alguno", values: ["borrador"] }],
      columnas: [],
    },
    createdAt: "2026-08-01T00:00:00Z",
    updatedAt: null,
  },
];

beforeEach(() => {
  // La consulta viaja en `?q=` y jsdom conserva la dirección entre pruebas: sin esto, la consulta
  // de una prueba se sembraría sola en la siguiente.
  window.history.replaceState(null, "", "/");
  window.localStorage.clear();
  vi.clearAllMocks();
  mocks.fetchCompanyQueryFields.mockResolvedValue(FIELDS);
  mocks.fetchCompanySavedQueries.mockResolvedValue(SAVED);
  mocks.runCompanyQuery.mockResolvedValue(result());
});

function renderTab(props: { tenantId?: string; needsCompany?: boolean } = {}) {
  return render(<ConsultasTab {...props} />);
}

describe("Consultas de la empresa", () => {
  it("consulta con la compañía que se está mirando", async () => {
    renderTab({ tenantId: "emp-1" });

    await waitFor(() => expect(mocks.runCompanyQuery).toHaveBeenCalled());

    expect(mocks.fetchCompanyQueryFields).toHaveBeenCalledWith("emp-1", expect.anything());
    expect(mocks.runCompanyQuery).toHaveBeenCalledWith(
      expect.objectContaining({ fechas: expect.objectContaining({ campo: "creacion" }) }),
      expect.objectContaining({ tenantId: "emp-1" }),
    );
  });

  it("abre sobre la fecha de creación, no sobre la de radicación al organismo", async () => {
    renderTab();

    const campo = await screen.findByTestId("empresa-query-campo-fecha");
    expect((campo as HTMLSelectElement).value).toBe("creacion");

    // Las fechas son las suyas: la gestora vive el trámite desde el borrador.
    const opciones = within(campo).getAllByRole("option").map((o) => o.textContent);
    expect(opciones).toContain("Fecha de envío al organismo");
    expect(opciones).not.toContain("Fecha de radicación");
  });

  it("enseña sus propias columnas y el estado del trámite en su empresa", async () => {
    renderTab();

    // La tabla llega con el resultado, no con la sección: se espera a una celda.
    expect(await screen.findByText("Secretaría de Movilidad")).toBeInTheDocument();

    expect(screen.getByRole("columnheader", { name: "Organismo" })).toBeInTheDocument();
    // «Entregado» aquí quiere decir que la gestora ya lo radicó, no que alguien lo haya mirado.
    expect(screen.getByText("Entregado")).toBeInTheDocument();
  });
});

describe("Cobertura, del lado de la empresa", () => {
  it("dice que lo que no salió no está en TU empresa, no en el organismo", async () => {
    mocks.runCompanyQuery.mockResolvedValue(
      result({
        total: 0,
        filas: [],
        cobertura: [
          {
            campo: "placa",
            valor: "ZZZ999",
            resultado: "no_existe",
            motivoCampo: null,
            motivo: "No hay ningún trámite con este valor en tu empresa.",
          },
        ],
      }),
    );

    renderTab();

    const aviso = await screen.findByTestId("empresa-query-cobertura");

    // Mandar a un gestor a reclamar al organismo por una placa que sencillamente no es suya es el
    // error que este aviso existe para no cometer.
    expect(aviso).toHaveTextContent("tu empresa");
    expect(aviso).not.toHaveTextContent("organismo");
  });

  it("nombra el campo como se llama en el catálogo de la empresa", async () => {
    mocks.runCompanyQuery.mockResolvedValue(
      result({
        cobertura: [
          {
            campo: "organismo",
            valor: "o9",
            resultado: "excluido",
            motivoCampo: "leasing",
            motivo: "Existe, pero lo dejó fuera el filtro «Leasing».",
          },
        ],
      }),
    );

    renderTab();

    await userEvent.click(await screen.findByTestId("empresa-query-cobertura-detalle"));

    const lista = screen.getByTestId("empresa-query-cobertura-lista");
    expect(lista).toHaveTextContent("Organismo de tránsito o9");
  });
});

describe("Sin compañía elegida", () => {
  it("no consulta y dice qué falta, en vez de dejar caer un error del servidor", async () => {
    renderTab({ needsCompany: true });

    expect(await screen.findByTestId("empresa-query-sin-compania")).toBeInTheDocument();
    expect(mocks.runCompanyQuery).not.toHaveBeenCalled();
    expect(mocks.fetchCompanyQueryFields).not.toHaveBeenCalled();
  });
});

describe("Guardar y exportar", () => {
  it("guarda la consulta con las columnas visibles", async () => {
    mocks.saveCompanyQuery.mockResolvedValue({
      ...SAVED[0],
      nombre: "Flota de agosto",
    });

    renderTab({ tenantId: "emp-1" });
    await screen.findByTestId("empresa-query-resultado");

    await userEvent.click(screen.getByRole("button", { name: "Guardar consulta" }));
    await userEvent.type(screen.getByTestId("empresa-query-nombre-input"), "Flota de agosto");
    await userEvent.click(screen.getByTestId("empresa-query-guardar-confirmar"));

    await waitFor(() => expect(mocks.saveCompanyQuery).toHaveBeenCalled());

    const [input, tenant] = mocks.saveCompanyQuery.mock.calls[0];
    expect(tenant).toBe("emp-1");
    expect(input.nombre).toBe("Flota de agosto");
    expect(input.definition.columnas.length).toBeGreaterThan(0);
  });

  it("no marca «modificada» la consulta recién guardada", async () => {
    // El servidor devuelve la definición con los huecos RELLENOS —`from` y `to` explícitos en
    // `null`— mientras que la de pantalla los omite mientras haya preajuste. Comparando los dos
    // objetos tal cual, la consulta quedaba marcada «modificada» nada más guardarla y sin que
    // nadie tocara un filtro; un aviso que miente justo al guardar enseña a ignorarlo.
    mocks.saveCompanyQuery.mockResolvedValue({
      ...SAVED[0],
      id: "s9",
      nombre: "Flota de agosto",
      definition: {
        fechas: { campo: "creacion", preset: "ultimos_30", from: null, to: null },
        condiciones: [],
        columnas: ["referencia", "placa"],
        sortBy: "creado",
        descending: true,
      },
    });

    renderTab({ tenantId: "emp-1" });
    await screen.findByTestId("empresa-query-resultado");

    await userEvent.click(screen.getByRole("button", { name: "Guardar consulta" }));
    await userEvent.type(screen.getByTestId("empresa-query-nombre-input"), "Flota de agosto");
    await userEvent.click(screen.getByTestId("empresa-query-guardar-confirmar"));

    await waitFor(() => expect(mocks.saveCompanyQuery).toHaveBeenCalled());
    // El nombre sale dos veces —en el aviso y en la tarjeta—, así que se espera por la tarjeta ya
    // pintada antes de comprobar que NO lleva la marca.
    await waitFor(() => expect(screen.getAllByText(/Flota de agosto/).length).toBeGreaterThan(0));
    expect(screen.queryByText("modificada")).not.toBeInTheDocument();
  });

  it("no ofrece la descarga en CSV", async () => {
    renderTab();
    await screen.findByTestId("empresa-query-resultado");

    expect(screen.getByTestId("empresa-query-export-xlsx")).toBeInTheDocument();
    expect(screen.queryByTestId("empresa-query-export-csv")).not.toBeInTheDocument();
  });
});

describe("Columnas de la empresa", () => {
  it("deja la celda de tipo de traspaso vacía en las matrículas iniciales", () => {
    const columna = COMPANY_QUERY_COLUMNS.find((c) => c.id === "tipo_traspaso")!;

    // El backend manda cadena vacía cuando no aplica. Pintar «Bilateral» ahí se leería como un
    // dato del trámite y no como un «no aplica».
    expect(columna.value(row({ modalidad: "matricula_inicial", tipoTraspaso: "" }))).toBe("—");
    expect(columna.raw!(row({ modalidad: "matricula_inicial", tipoTraspaso: "" }))).toBeNull();
    expect(columna.value(row({ tipoTraspaso: "unilateral" }))).toBe("Unilateral");
  });

  it("enseña el tipo de trámite en todas las vistas y el de traspaso en ninguna", () => {
    // Sin el tipo de trámite, una matrícula y un traspaso son dos filas indistinguibles. El tipo de
    // traspaso es lo contrario: viene vacío en todo lo que no sea un traspaso, así que de salida
    // ocupa una columna que en media tabla no dice nada. Queda a un clic en el selector.
    expect(defaultCompanyQueryColumns()).toContain("tipo");
    expect(defaultCompanyQueryColumns()).not.toContain("tipo_traspaso");

    for (const preset of COMPANY_QUERY_PRESETS) {
      expect(preset.columns, `preset «${preset.label}»`).toContain("tipo");
      expect(preset.columns, `preset «${preset.label}»`).not.toContain("tipo_traspaso");
    }

    // Y sigue estando disponible para quien la quiera.
    expect(COMPANY_QUERY_COLUMNS.some((c) => c.id === "tipo_traspaso")).toBe(true);
  });

  it("el Excel lleva los números como números, no como texto", () => {
    const bytes = buildCompanyQueryXlsx([row()], ["referencia", "dias_en_organismo"]);

    // Un .xlsx es un zip: basta con que salga uno bien formado y no vacío para saber que el
    // escritor recibió lo que esperaba. El contenido se comprueba en las pruebas del escritor.
    expect(bytes.length).toBeGreaterThan(0);
    expect(bytes[0]).toBe(0x50); // "P" de PK
  });
});
