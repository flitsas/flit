// Consulta personalizada de la empresa, de punta a punta: filas con distinto número de actores,
// y el archivo que sale con el ancho que dijo el servidor (`maxActoresPorRol`).
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type { XlsxSheet } from "@/lib/xlsx";
import type {
  ActorValidacionDto,
  CompanyQueryResult,
  CompanyQueryRow,
  RolActor,
} from "@/lib/api/company-queries";

const mocks = vi.hoisted(() => ({
  fetchCompanyQueryFields: vi.fn(),
  fetchCompanySavedQueries: vi.fn(),
  runCompanyQuery: vi.fn(),
}));
const salida = vi.hoisted(() => ({
  hojas: [] as unknown[],
  descargas: [] as { nombre: string; contenido: unknown }[],
}));

vi.mock("@/lib/api/company-queries", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/company-queries")>();
  return { ...actual, ...mocks };
});
vi.mock("@/lib/xlsx", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/xlsx")>();
  return {
    ...actual,
    buildXlsx: (sheet: XlsxSheet) => {
      salida.hojas.push(sheet);
      return actual.buildXlsx(sheet);
    },
  };
});
vi.mock("@/components/consultas/export", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/components/consultas/export")>();
  return {
    ...actual,
    download: (contenido: BlobPart, nombre: string) => salida.descargas.push({ nombre, contenido }),
  };
});

import { ConsultasTab } from "@/components/atom/modules/_reportes/tabs/ConsultasTab";

function actor(rol: RolActor, ordinal: number, over: Partial<ActorValidacionDto> = {}): ActorValidacionDto {
  return {
    rol,
    ordinal,
    nombre: `${rol} ${ordinal}`,
    tipoDocumento: "CC",
    documento: `${ordinal}0001`,
    estadoValidacion: "aprobado",
    motivoRechazo: null,
    intentos: 1,
    ...over,
  };
}

function row(id: string, actores: ActorValidacionDto[]): CompanyQueryRow {
  return {
    procedureInstanceId: id,
    referenceNumber: `REF-${id}`,
    placa: "ABC123",
    transitOfficeName: "Secretaría",
    procedureTypeName: "Traspaso",
    status: "entregado",
    actores,
  } as CompanyQueryRow;
}

// Fila A: traspaso con 2 compradores y 1 vendedor «Sin validar». Fila B: matrícula, solo propietario.
const FILA_A = row("a", [
  actor("comprador", 2, { estadoValidacion: "rechazado", intentos: 3, motivoRechazo: "El rostro no coincide con el documento." }),
  actor("comprador", 1),
  actor("vendedor", 1, { estadoValidacion: "sin_validacion", intentos: 0 }),
]);
const FILA_B = row("b", [actor("propietario", 1, { nombre: "Dueño Uno" })]);

function respuesta(over: Partial<CompanyQueryResult> = {}): CompanyQueryResult {
  return {
    total: 2,
    page: 1,
    pageSize: 25,
    desde: "2026-07-07",
    hasta: "2026-08-05",
    totalPeriodoAnterior: 0,
    filas: [FILA_A, FILA_B],
    cobertura: [],
    // El servidor calcula el máximo sobre TODO el filtro: puede ser mayor que lo que hay en la página.
    maxActoresPorRol: { comprador: 2, vendedor: 1, propietario: 1 },
    ...over,
  };
}

beforeEach(() => {
  window.history.replaceState(null, "", "/");
  window.localStorage.clear();
  vi.clearAllMocks();
  salida.hojas.length = 0;
  salida.descargas.length = 0;
  mocks.fetchCompanyQueryFields.mockResolvedValue([]);
  mocks.fetchCompanySavedQueries.mockResolvedValue([]);
  mocks.runCompanyQuery.mockResolvedValue(respuesta());
});

const OCHO = [
  "Comprador", "Validación comprador", "Intentos comprador", "Motivo rechazo comprador",
  "Vendedor", "Validación vendedor", "Intentos vendedor", "Motivo rechazo vendedor",
];

async function abrir(columnas: string[] = OCHO) {
  render(<ConsultasTab />);
  await waitFor(() => expect(screen.getByTestId("empresa-query-export-xlsx")).toBeEnabled());
  await userEvent.click(screen.getByTestId("empresa-query-column-picker"));
  const panel = screen.getByTestId("empresa-query-column-picker-panel");
  for (const columna of columnas) {
    await userEvent.click(within(panel).getByLabelText(columna));
  }
  return panel;
}

const encabezados = () => (salida.hojas.at(-1) as XlsxSheet).columns.map((c) => c.header);

describe("Consulta personalizada — columnas por rol", () => {
  it("muestra las 8 columnas juntas y una línea por actor del rol", async () => {
    await abrir();

    const cabeceras = screen.getAllByRole("columnheader").map((h) => h.textContent);
    const i = cabeceras.indexOf("Comprador");
    expect(cabeceras.slice(i, i + 8)).toEqual(OCHO);

    const [filaA, filaB] = screen.getAllByRole("row").slice(1);
    // Fila A: 2 compradores en la misma celda, 1 vendedor; las 4 columnas alineadas.
    for (const campo of ["persona", "validacion", "intentos", "motivo"]) {
      expect(within(filaA).getAllByTestId(`actor-${campo}`)).toHaveLength(3);
    }
    expect(within(filaA).getByText("El rostro no coincide con el documento.")).toBeInTheDocument();
    expect(within(filaA).getByText("Sin validar")).toBeInTheDocument();
    // Fila B: el propietario va en Comprador; el rol Vendedor dice «—» en sus 4 columnas.
    expect(within(filaB).getByText("Dueño Uno")).toBeInTheDocument();
    const celdas = within(filaB).getAllByRole("cell");
    const v = cabeceras.indexOf("Vendedor");
    expect(celdas.slice(v, v + 4).map((c) => c.textContent)).toEqual(["—", "—", "—", "—"]);
  });

  it("el Excel sale con el ancho del servidor, bloque por persona y en su sitio", async () => {
    await abrir();
    await userEvent.click(screen.getByTestId("empresa-query-export-xlsx"));
    await waitFor(() => expect(salida.hojas).toHaveLength(1));

    const h = encabezados();
    const i = h.indexOf("Comprador");
    // comprador = max(2, propietario 1) = 2 bloques; vendedor = 1 bloque; las 8 columnas siguen juntas.
    expect(h.slice(i, i + 12)).toEqual([
      "Comprador", "Validación comprador", "Intentos comprador", "Motivo rechazo comprador",
      "Comprador 2", "Validación comprador 2", "Intentos comprador 2", "Motivo rechazo comprador 2",
      "Vendedor", "Validación vendedor", "Intentos vendedor", "Motivo rechazo vendedor",
    ]);
    expect(h.some((x) => /Documento/.test(x))).toBe(false);

    const hoja = salida.hojas[0] as XlsxSheet;
    expect(hoja.rows[0].slice(i, i + 12)).toEqual([
      "comprador 1", "Aprobado", 1, "",
      "comprador 2", "Rechazado", 3, "El rostro no coincide con el documento.",
      "vendedor 1", "Sin validar", 0, "",
    ]);
    // Matrícula: propietario en el bloque 1 de comprador, el resto vacío.
    expect(hoja.rows[1].slice(i, i + 12)).toEqual([
      "Dueño Uno", "Aprobado", 1, "", null, null, null, null, null, null, null, null,
    ]);
  });

  it("la columna oculta no se exporta", async () => {
    await abrir(["Comprador", "Validación comprador", "Motivo rechazo comprador", "Vendedor"]);
    await userEvent.click(screen.getByTestId("empresa-query-export-xlsx"));
    await waitFor(() => expect(salida.hojas).toHaveLength(1));

    const h = encabezados();
    expect(h).toContain("Validación comprador 2");
    expect(h.some((x) => /Intentos/.test(x))).toBe(false);
    expect(h).not.toContain("Validación vendedor");
  });

  it("una consulta que solo pide Comprador sale con Comprador y Comprador 2", async () => {
    await abrir(["Comprador"]);
    await userEvent.click(screen.getByTestId("empresa-query-export-xlsx"));
    await waitFor(() => expect(salida.hojas).toHaveLength(1));

    const h = encabezados();
    expect(h.filter((x) => /Comprador|Vendedor/.test(x))).toEqual(["Comprador", "Comprador 2"]);
  });

  it("sin maxActoresPorRol no inventa el ancho: solo Comprador y Vendedor con el texto de siempre", async () => {
    mocks.runCompanyQuery.mockResolvedValue(respuesta({ maxActoresPorRol: undefined }));
    await abrir();
    await userEvent.click(screen.getByTestId("empresa-query-export-xlsx"));
    await waitFor(() => expect(salida.hojas).toHaveLength(1));

    const h = encabezados();
    expect(h.filter((x) => /Comprador|Vendedor|Validación|Intentos|Motivo/.test(x))).toEqual([
      "Comprador",
      "Vendedor",
    ]);
  });

  it("en un export por lotes todas las partes salen con el mismo ancho", async () => {
    const pageSize = 200;
    const total = pageSize * 26; // 5.200 filas → 2 archivos
    mocks.runCompanyQuery.mockImplementation(async (_d: unknown, { page }: { page: number }) => {
      const quedan = total - (page - 1) * pageSize;
      const filas = Array.from({ length: Math.max(0, Math.min(pageSize, quedan)) }, (_, i) =>
        // La 1ª parte solo tiene 1 comprador por fila; el servidor dice que en el filtro hay hasta 3.
        row(`p${page}-${i}`, [actor("comprador", 1)]),
      );
      return respuesta({ total, filas, maxActoresPorRol: { comprador: 3, vendedor: 1, propietario: 0 } });
    });
    await abrir();
    await userEvent.click(screen.getByTestId("empresa-query-export-xlsx"));
    await waitFor(() => expect(salida.hojas).toHaveLength(2), { timeout: 20000 });

    const [uno, dos] = (salida.hojas as XlsxSheet[]).map((h) => h.columns.map((c) => c.header));
    expect(uno).toEqual(dos);
    expect(uno).toContain("Motivo rechazo comprador 3");
  }, 30000);

  it("nada de lo exportado revela cómo se validó", async () => {
    await abrir();
    await userEvent.click(screen.getByTestId("empresa-query-export-xlsx"));
    await waitFor(() => expect(salida.hojas).toHaveLength(1));
    expect(JSON.stringify(salida.hojas[0])).not.toMatch(/kyverum|manual|proveedor|fuente/i);
  });
});
