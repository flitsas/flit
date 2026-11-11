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

// Fila A: 1 comprador, 1 vendedor, 1 propietario sin validar. Fila B: 3 vendedores y 1 comprador.
const FILA_A = row("a", [
  actor("comprador", 1),
  actor("vendedor", 1, { estadoValidacion: "rechazado", intentos: 2, motivoRechazo: "Documento ilegible" }),
  actor("propietario", 1, { estadoValidacion: "sin_validacion", intentos: 0 }),
]);
const FILA_B = row("b", [
  actor("vendedor", 3),
  actor("vendedor", 1),
  actor("comprador", 1),
  actor("vendedor", 2),
]);

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
    maxActoresPorRol: { comprador: 1, vendedor: 3, propietario: 1 },
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

async function abrirConIdentidad() {
  render(<ConsultasTab />);
  await waitFor(() => expect(screen.getByTestId("empresa-query-export-xlsx")).toBeEnabled());
  await userEvent.click(screen.getByTestId("empresa-query-column-picker"));
  const panel = screen.getByTestId("empresa-query-column-picker-panel");
  for (const columna of ["Persona", "Validación", "Intentos", "Motivo de rechazo"]) {
    await userEvent.click(within(panel).getByLabelText(columna));
  }
  return panel;
}

describe("Consulta personalizada — validación de identidad", () => {
  it("pinta cada actor en su línea, alineado entre las cuatro columnas", async () => {
    await abrirConIdentidad();

    const filas = screen.getAllByRole("row").slice(1);
    // Fila A: 3 actores; fila B: 4 actores, en el orden comprador → vendedor 1, 2, 3.
    expect(within(filas[0]).getAllByTestId("actor-persona")).toHaveLength(3);
    expect(within(filas[1]).getAllByTestId("actor-persona")).toHaveLength(4);
    for (const campo of ["validacion", "intentos", "motivo"]) {
      expect(within(filas[1]).getAllByTestId(`actor-${campo}`)).toHaveLength(4);
    }
    expect(within(filas[0]).getByText("Sin validar")).toBeInTheDocument();
    expect(within(filas[0]).getByText("Documento ilegible")).toBeInTheDocument();
  });

  it("el Excel sale con el ancho del servidor: 1 + 3 + 1 bloques de 5 columnas", async () => {
    await abrirConIdentidad();
    await userEvent.click(screen.getByTestId("empresa-query-export-xlsx"));
    await waitFor(() => expect(salida.hojas).toHaveLength(1));

    const hoja = salida.hojas[0] as XlsxSheet;
    const encabezados = hoja.columns.map((c) => c.header);
    const identidad = encabezados.slice(encabezados.indexOf("Comprador"));
    expect(identidad).toHaveLength(25);
    expect(identidad).toContain("Motivo rechazo vendedor 3");
    expect(identidad.at(-1)).toBe("Motivo rechazo propietario");

    // Fila A solo tiene 1 vendedor: los bloques «vendedor 2» y «vendedor 3» van vacíos.
    const celdasA = hoja.rows[0].slice(encabezados.indexOf("Comprador"));
    expect(celdasA.slice(0, 5)).toEqual(["comprador 1", "CC 10001", "Aprobado", 1, ""]);
    expect(celdasA.slice(5, 10)).toEqual(["vendedor 1", "CC 10001", "Rechazado", 2, "Documento ilegible"]);
    expect(celdasA.slice(10, 20).every((c) => c === null)).toBe(true);
    expect(celdasA.slice(20, 25)).toEqual(["propietario 1", "CC 10001", "Sin validar", 0, ""]);

    // Fila B tiene 3 vendedores (llegaron desordenados) y ningún propietario.
    const celdasB = hoja.rows[1].slice(encabezados.indexOf("Comprador"));
    expect([celdasB[5], celdasB[10], celdasB[15]]).toEqual(["vendedor 1", "vendedor 2", "vendedor 3"]);
    expect(celdasB.slice(20, 25).every((c) => c === null)).toBe(true);
  });

  it("la columna oculta no se exporta", async () => {
    const panel = await abrirConIdentidad();
    await userEvent.click(within(panel).getByLabelText("Motivo de rechazo"));
    await userEvent.click(within(panel).getByLabelText("Intentos"));

    await userEvent.click(screen.getByTestId("empresa-query-export-xlsx"));
    await waitFor(() => expect(salida.hojas).toHaveLength(1));

    const encabezados = (salida.hojas[0] as XlsxSheet).columns.map((c) => c.header);
    expect(encabezados).toContain("Validación vendedor 3");
    expect(encabezados).toContain("Documento propietario");
    expect(encabezados.some((h) => /Motivo rechazo|Intentos/.test(h))).toBe(false);
    // 1 + 3 + 1 bloques de 3 columnas (Persona → 2, Validación → 1).
    expect(encabezados.filter((h) => /comprador|vendedor|propietario/i.test(h))).toHaveLength(15);
  });

  it("si el servidor no manda maxActoresPorRol, no se inventa el ancho", async () => {
    mocks.runCompanyQuery.mockResolvedValue(respuesta({ maxActoresPorRol: undefined }));
    await abrirConIdentidad();
    await userEvent.click(screen.getByTestId("empresa-query-export-xlsx"));
    await waitFor(() => expect(salida.hojas).toHaveLength(1));

    const encabezados = (salida.hojas[0] as XlsxSheet).columns.map((c) => c.header);
    expect(encabezados).not.toContain("Comprador");
    expect(encabezados.some((h) => /Validación|Intentos|Motivo/.test(h))).toBe(false);
  });
});
