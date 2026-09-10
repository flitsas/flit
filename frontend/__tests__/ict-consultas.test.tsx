// Consultas de ICT (HU #11610): el usuario arma su propia búsqueda sobre los pre-trámites del
// pipeline de Integración con Terceros, la guarda y la exporta.
//
// Mismo criterio que ot-consultas.test.tsx: lo que más importa es el aviso de cobertura (un
// resultado con menos filas de las pedidas no debe leerse como "se perdió un dato") y que, a
// diferencia de OT/empresa, el botón "Programar informe" NO aparece — el backend todavía no tiene
// un SavedQueryScope para consultas guardadas de ICT.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";

import type { IctQueryRow } from "@/lib/api/ict-queries";
import type { QueryField, SavedQuery } from "@/lib/api/queries";

const mocks = vi.hoisted(() => ({
  fetchIctQueryFields: vi.fn(),
  fetchIctSavedQueries: vi.fn(),
  runIctQuery: vi.fn(),
  saveIctQuery: vi.fn(),
  deleteIctSavedQuery: vi.fn(),
}));

vi.mock("@/lib/api/ict-queries", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/ict-queries")>();
  return { ...actual, ...mocks };
});

import { IctQueriesTab } from "@/components/atom/modules/_ict/IctQueriesTab";
import {
  buildIctQueryCsv,
  ictEstadoMeta,
} from "@/components/atom/modules/_ict/ict-query-columns";

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
];

function row(overrides: Partial<IctQueryRow> = {}): IctQueryRow {
  return {
    id: "row-1",
    transactionNumber: 100,
    radicado: "ICT-0001",
    placa: "ABC123",
    vin: "VIN0001",
    tenantId: "c1",
    tenantNombre: "Distribuidora del Valle S.A.S.",
    tipoTramite: "Matrícula inicial",
    estado: "en_validacion_negocio",
    tieneNovedades: false,
    tieneBorrador: false,
    prioritario: false,
    secretaria: "Secretaría de Movilidad",
    clienteIntegracion: "Concesionario X",
    comentarios: null,
    procedureInstanceId: null,
    registradoEn: "2026-08-01T14:00:00Z",
    validacionNegocioEn: null,
    validacionExternaEn: null,
    ...overrides,
  };
}

const SAVED: SavedQuery[] = [
  {
    id: "s1",
    nombre: "Con novedades",
    descripcion: null,
    deFabrica: false,
    definition: {
      fechas: { campo: "registro", preset: "ultimos_30" },
      condiciones: [],
      columnas: [],
    },
    createdAt: "2026-08-01T00:00:00Z",
    updatedAt: null,
  },
];

describe("IctQueriesTab", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchIctQueryFields.mockResolvedValue(FIELDS);
    mocks.fetchIctSavedQueries.mockResolvedValue(SAVED);
    mocks.runIctQuery.mockResolvedValue({
      total: 1,
      page: 1,
      pageSize: 25,
      desde: "2026-07-07",
      hasta: "2026-08-05",
      totalPeriodoAnterior: 0,
      filas: [row()],
      cobertura: [],
    });
  });

  it("carga el catálogo y las consultas guardadas de ICT, no las del organismo ni la empresa", async () => {
    render(<IctQueriesTab tenantId="c1" />);

    await waitFor(() => expect(mocks.fetchIctQueryFields).toHaveBeenCalledWith("c1", expect.anything()));
    expect(mocks.fetchIctSavedQueries).toHaveBeenCalledWith("c1", expect.anything());
    await waitFor(() => expect(screen.getByText("Con novedades")).toBeInTheDocument());
  });

  it("ejecuta la consulta y pinta el resultado sobre el pipeline de ICT", async () => {
    render(<IctQueriesTab tenantId="c1" />);

    await waitFor(() => expect(mocks.runIctQuery).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByText("ICT-0001")).toBeInTheDocument());
  });

  it("no muestra el botón de calendario: no hay onScheduleQuery porque el backend aún no soporta programar consultas de ICT", async () => {
    render(<IctQueriesTab tenantId="c1" />);

    await waitFor(() => expect(screen.getByText("Con novedades")).toBeInTheDocument());
    expect(
      screen.queryByRole("button", { name: /Programar informe de Con novedades/i }),
    ).not.toBeInTheDocument();
  });

  it("no lanza cuando solo hay consultas de fábrica", async () => {
    mocks.fetchIctSavedQueries.mockResolvedValue([]);

    expect(() => render(<IctQueriesTab tenantId="c1" />)).not.toThrow();
    await waitFor(() => expect(mocks.fetchIctSavedQueries).toHaveBeenCalled());
  });
});

describe("ictEstadoMeta", () => {
  it("traduce cada estado del pipeline a una etiqueta legible", () => {
    expect(ictEstadoMeta("recibido").label).toBe("Recibido");
    expect(ictEstadoMeta("en_validacion_negocio").label).toBe("En validación de negocio");
    expect(ictEstadoMeta("con_novedades").label).toBe("Con novedades");
    expect(ictEstadoMeta("borrador_creado").label).toBe("Borrador creado");
  });

  it("no revienta con un estado que no conoce: lo muestra tal cual", () => {
    expect(ictEstadoMeta("estado_nuevo").label).toBe("estado_nuevo");
  });
});

describe("buildIctQueryCsv", () => {
  it("agrega el aviso de cobertura al final del CSV, no lo pierde", () => {
    const csv = buildIctQueryCsv([row()], ["radicado", "placa"], ["2 placas no existen"]);
    expect(csv).toContain("2 placas no existen");
  });
});
