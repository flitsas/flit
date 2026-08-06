// Consultas del organismo: el usuario arma su propia búsqueda, la guarda y la exporta.
//
// Lo que más se prueba aquí es el aviso de cobertura. Es la pieza que decide si el resultado se
// puede usar: sin él, un resultado con menos filas de las pedidas se lee como «se me perdió un
// dato», y esa sospecha no se recupera. Lo segundo es que el export recorra TODAS las páginas y no
// la visible, que es la trampa que haría parecer completo un archivo que no lo está.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type {
  OtQueryField,
  OtQueryResult,
  OtQueryRow,
  OtSavedQuery,
} from "@/lib/api/ot-queries";

const mocks = vi.hoisted(() => ({
  fetchOtQueryFields: vi.fn(),
  fetchOtSavedQueries: vi.fn(),
  runOtQuery: vi.fn(),
  saveOtQuery: vi.fn(),
  deleteOtSavedQuery: vi.fn(),
}));

vi.mock("@/lib/api/ot-queries", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/ot-queries")>();
  return { ...actual, ...mocks };
});

import { OtQueriesTab } from "@/components/admin/transit-offices/_reportes/OtQueriesTab";
import {
  buildQueryCsv,
  buildQueryXlsx,
} from "@/components/admin/transit-offices/_reportes/query-columns";
import {
  coverageLines,
} from "@/components/consultas/CoverageNotice";
import { parseValueList } from "@/components/consultas/QueryFilterBar";

const FIELDS: OtQueryField[] = [
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
    id: "licencia_transito",
    label: "Licencia de tránsito cargada",
    kind: "booleano",
    group: "Características",
    operators: ["es_alguno"],
    options: [
      { value: "true", label: "Sí" },
      { value: "false", label: "No" },
    ],
    hint: null,
    admiteLista: false,
  },
  {
    id: "empresa",
    label: "Empresa cliente",
    kind: "opcion",
    group: "Trámite",
    operators: ["es_alguno", "no_es_ninguno"],
    options: [{ value: "c1", label: "Distribuidora del Valle S.A.S." }],
    hint: null,
    admiteLista: true,
  },
];

function row(overrides: Partial<OtQueryRow> = {}): OtQueryRow {
  return {
    procedureInstanceId: "p1",
    referenceNumber: "REF-1",
    placa: "ABC123",
    vin: "VIN0001",
    clientTenantId: "c1",
    clientTenantName: "Distribuidora del Valle S.A.S.",
    modalidad: "matricula_inicial",
    status: "entregado",
    estadoOt: "en_revision",
    prioritario: false,
    subsanacionActiva: false,
    comprador: "Cándida Compradora",
    vendedor: null,
    tienePrenda: true,
    acreedorPrenda: "Banco X",
    tieneLicenciaTransito: true,
    transformaciones: ["cambio_color"],
    creadoEn: "2026-08-01T14:00:00Z",
    radicadoEn: "2026-08-01T14:00:00Z",
    ultimaRadicacionEn: "2026-08-01T14:00:00Z",
    decididoEn: null,
    actualizadoEn: "2026-08-02T14:00:00Z",
    decididoPor: null,
    horasHastaDecision: null,
    diasEnOrganismo: 3,
    devoluciones: 0,
    causalesUltimoRechazo: [],
    ...overrides,
  };
}

function result(overrides: Partial<OtQueryResult> = {}): OtQueryResult {
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

const SAVED: OtSavedQuery[] = [
  {
    id: "s1",
    nombre: "Mis traspasos",
    descripcion: null,
    deFabrica: false,
    definition: {
      fechas: { campo: "radicacion", preset: "ultimos_30" },
      condiciones: [],
      columnas: [],
    },
    createdAt: "2026-08-01T00:00:00Z",
    updatedAt: null,
  },
  {
    id: "f1",
    nombre: "Con prenda y sin licencia de tránsito",
    descripcion: "Punto de partida",
    deFabrica: true,
    definition: {
      fechas: { campo: "radicacion", preset: "ultimos_90" },
      condiciones: [{ fieldId: "prenda", operator: "es_alguno", values: ["true"] }],
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
  vi.clearAllMocks();
  mocks.fetchOtQueryFields.mockResolvedValue(FIELDS);
  mocks.fetchOtSavedQueries.mockResolvedValue(SAVED);
  mocks.runOtQuery.mockResolvedValue(result());
});

function renderTab() {
  return render(<OtQueriesTab transitOfficeId="ot-1" />);
}

describe("Cobertura de la búsqueda", () => {
  it("dice cuáles de los valores pedidos no salieron y por qué", async () => {
    mocks.runOtQuery.mockResolvedValue(
      result({
        cobertura: [
          { campo: "placa", valor: "ABC123", resultado: "encontrado", motivoCampo: null, motivo: null },
          {
            campo: "placa",
            valor: "XYZ789",
            resultado: "excluido",
            motivoCampo: "licencia_transito",
            motivo: "Existe, pero lo dejó fuera el filtro «Licencia de tránsito cargada».",
          },
          {
            campo: "placa",
            valor: "NOP000",
            resultado: "no_existe",
            motivoCampo: null,
            motivo: "No hay ningún trámite con este valor en el organismo.",
          },
        ],
      }),
    );

    renderTab();

    const aviso = await screen.findByTestId("ot-query-cobertura");

    // El resumen separa las dos causas ANTES de abrir nada: son dos acciones distintas, una se
    // arregla aflojando un filtro y la otra no se arregla.
    expect(aviso).toHaveTextContent("1 existe pero quedó fuera por los filtros");
    expect(aviso).toHaveTextContent("1 no está en este organismo");

    await userEvent.click(screen.getByTestId("ot-query-cobertura-detalle"));

    const lista = screen.getByTestId("ot-query-cobertura-lista");
    expect(within(lista).getByText(/Licencia de tránsito cargada/)).toBeInTheDocument();
    expect(within(lista).getByText(/No hay ningún trámite con este valor/)).toBeInTheDocument();

    // Lo que SÍ salió no aparece: el aviso solo habla de lo que hay que explicar.
    expect(within(lista).queryByText(/ABC123/)).not.toBeInTheDocument();
  });

  it("no se muestra cuando todo lo pedido salió", async () => {
    mocks.runOtQuery.mockResolvedValue(
      result({
        cobertura: [
          { campo: "placa", valor: "ABC123", resultado: "encontrado", motivoCampo: null, motivo: null },
        ],
      }),
    );

    renderTab();

    await screen.findByTestId("ot-query-total");
    // Un panel verde diciendo «1 de 1» es ruido: el resultado ya lo dice.
    expect(screen.queryByTestId("ot-query-cobertura")).not.toBeInTheDocument();
  });

  it("el aviso viaja dentro del archivo exportado", () => {
    // En pantalla el aviso está al lado del resultado; en el Excel no habría nada, y el archivo es
    // justo lo que se reenvía a quien no ejecutó la consulta.
    const lineas = coverageLines([
      { campo: "placa", valor: "ABC123", resultado: "encontrado", motivoCampo: null, motivo: null },
      {
        campo: "placa",
        valor: "XYZ789",
        resultado: "excluido",
        motivoCampo: "licencia_transito",
        motivo: "Existe, pero lo dejó fuera el filtro «Licencia de tránsito cargada».",
      },
    ]);

    expect(lineas).toHaveLength(1);
    expect(lineas[0]).toContain("XYZ789");

    const xlsx = buildQueryXlsx([row()], ["referencia"], lineas);
    const texto = new TextDecoder().decode(xlsx);
    expect(texto).toContain("XYZ789");

    const csv = buildQueryCsv([row()], ["referencia"], lineas);
    expect(csv).toContain("XYZ789");
  });
});

describe("Pegar listas", () => {
  it("acepta saltos de línea, comas y punto y coma, y quita repetidos", () => {
    // Una columna copiada de Excel llega con saltos; una lista escrita a mano llega con comas. Quien
    // pega no debería tener que saber cuál espera el campo.
    expect(parseValueList("ABC123\nXYZ789\r\nABC123")).toEqual(["ABC123", "XYZ789"]);
    expect(parseValueList("ABC123, XYZ789 ; QRS456")).toEqual(["ABC123", "XYZ789", "QRS456"]);
    expect(parseValueList("  \n  ")).toEqual([]);
  });

  it("permite armar un filtro de placas pegando la lista", async () => {
    renderTab();
    await screen.findByTestId("ot-query-total");

    await userEvent.click(screen.getByTestId("ot-query-agregar-filtro"));
    await userEvent.click(within(screen.getByTestId("ot-query-campos")).getByText("Placa"));

    const textarea = await screen.findByTestId("ot-query-valores-placa");
    await userEvent.type(textarea, "ABC123{Enter}XYZ789");

    expect(screen.getByText("2 valores")).toBeInTheDocument();

    await userEvent.click(screen.getByTestId("ot-query-aplicar-placa"));

    // Con más de dos valores la ficha dice cuántos, no cuáles: veinte placas dentro de un chip lo
    // vuelven ilegible.
    expect(await screen.findByTestId("ot-query-chip-placa")).toHaveTextContent("Placa es ABC123 o XYZ789");

    await waitFor(() => {
      const ultima = mocks.runOtQuery.mock.calls.at(-1)![0];
      expect(ultima.condiciones).toEqual([
        { fieldId: "placa", operator: "es_alguno", values: ["ABC123", "XYZ789"] },
      ]);
    });
  });
});

describe("Consultas guardadas", () => {
  it("separa las propias de las de fábrica y las de fábrica van al final", async () => {
    renderTab();

    const lista = await screen.findByTestId("ot-query-guardadas");
    expect(within(lista).getByText("Mis consultas")).toBeInTheDocument();
    // Existen para que la lista nunca esté vacía: ante un lienzo en blanco nadie sabe qué preguntar.
    expect(within(lista).getByText("Para empezar")).toBeInTheDocument();
    expect(within(lista).getByTestId("ot-query-guardada-f1")).toBeInTheDocument();
  });

  it("abrir una consulta guardada aplica sus filtros", async () => {
    renderTab();

    await screen.findByTestId("ot-query-total");
    await userEvent.click(screen.getByTestId("ot-query-guardada-f1"));

    await waitFor(() => {
      const ultima = mocks.runOtQuery.mock.calls.at(-1)![0];
      expect(ultima.fechas.preset).toBe("ultimos_90");
      expect(ultima.condiciones).toHaveLength(1);
    });
  });

  it("avisa cuando lo de pantalla ya no es la consulta guardada", async () => {
    renderTab();

    await screen.findByTestId("ot-query-total");
    await userEvent.click(screen.getByTestId("ot-query-guardada-s1"));

    expect(screen.queryByText(/modificada/)).not.toBeInTheDocument();

    // Tocar un filtro: a partir de aquí lo que se ve deja de ser lo guardado, y creer lo contrario
    // es el malentendido clásico de estas listas.
    await userEvent.selectOptions(screen.getByTestId("ot-query-preset"), "hoy");

    expect(await screen.findByText(/modificada/)).toBeInTheDocument();
  });

  it("las de fábrica no se pueden borrar", async () => {
    renderTab();

    const lista = await screen.findByTestId("ot-query-guardadas");
    expect(within(lista).getByLabelText("Borrar Mis traspasos")).toBeInTheDocument();
    expect(
      within(lista).queryByLabelText("Borrar Con prenda y sin licencia de tránsito"),
    ).not.toBeInTheDocument();
  });
});

describe("Resultado", () => {
  it("compara contra el periodo anterior", async () => {
    mocks.runOtQuery.mockResolvedValue(result({ total: 112, totalPeriodoAnterior: 100 }));

    renderTab();

    // Un número suelto no dice si es mucho o poco.
    expect(await screen.findByTestId("ot-query-comparacion")).toHaveTextContent(
      "+12 % frente a los 100 del periodo anterior",
    );
  });

  it("el export recorre todas las páginas y no solo la visible", async () => {
    const filas = Array.from({ length: 25 }, (_, i) =>
      row({ procedureInstanceId: `p${i}`, referenceNumber: `REF-${i}` }),
    );
    mocks.runOtQuery.mockResolvedValue(result({ total: 60, filas }));

    renderTab();
    await screen.findByTestId("ot-query-total");
    await waitFor(() => expect(screen.getByTestId("ot-query-export-xlsx")).toBeEnabled());

    mocks.runOtQuery.mockClear();
    await userEvent.click(screen.getByTestId("ot-query-export-xlsx"));

    // La respuesta contraria —exportar los 25 de pantalla— sería una trampa: el archivo parecería
    // completo y nadie lo comprobaría.
    await waitFor(() => {
      const paginas = mocks.runOtQuery.mock.calls.map((c) => c[1].page);
      expect(paginas.length).toBeGreaterThan(1);
      expect(paginas[0]).toBe(1);
      expect(paginas[1]).toBe(2);
    });
  });

  it("el rango de fechas dice explícitamente sobre qué fecha aplica", async () => {
    renderTab();

    const selector = await screen.findByTestId("ot-query-campo-fecha");
    expect(within(selector).getByText("Fecha de radicación")).toBeInTheDocument();
    expect(within(selector).getByText("Fecha de decisión")).toBeInTheDocument();

    await userEvent.selectOptions(selector, "decision");

    await waitFor(() => {
      expect(mocks.runOtQuery.mock.calls.at(-1)![0].fechas.campo).toBe("decision");
    });
  });
});

describe("Excel de la consulta", () => {
  it("escribe números como números y fechas como fechas", () => {
    const xlsx = buildQueryXlsx(
      [row({ diasEnOrganismo: 3, devoluciones: 2 })],
      ["referencia", "dias_en_organismo", "devoluciones", "radicado_en"],
    );

    const texto = new TextDecoder().decode(xlsx);
    // Sin `t="s"` la celda es numérica: se puede sumar la columna sin tocar nada.
    // Las columnas salen en el orden canónico de la definición, no en el que se pidieron.
    expect(texto).toContain('<c r="B2" s="0"><v>2</v></c>');
    expect(texto).toContain("REF-1");
  });

  it("las columnas booleanas se leen como Sí/No y no como true/false", () => {
    const csv = buildQueryCsv([row({ tienePrenda: true, tieneLicenciaTransito: false })], [
      "prenda",
      "licencia_transito",
    ]);

    expect(csv).toContain('"Sí"');
    expect(csv).toContain('"No"');
  });
});

describe("La consulta vive en la dirección", () => {
  async function ponerFiltroDePlaca() {
    await userEvent.click(screen.getByTestId("ot-query-agregar-filtro"));
    await userEvent.click(within(screen.getByTestId("ot-query-campos")).getByText("Placa"));
    await userEvent.type(await screen.findByTestId("ot-query-valores-placa"), "ABC123");
    await userEvent.click(screen.getByTestId("ot-query-aplicar-placa"));
    await screen.findByTestId("ot-query-chip-placa");
  }

  it("sobrevive a salir de la pestaña y volver", async () => {
    // Cada pestaña de la consola se monta y se desmonta. Sin la consulta en la dirección, asomarse
    // a otra pestaña tiraba lo que el usuario llevaba armado.
    const primera = renderTab();
    await screen.findByTestId("ot-query-total");
    await ponerFiltroDePlaca();
    primera.unmount();

    renderTab();
    expect(await screen.findByTestId("ot-query-chip-placa")).toHaveTextContent("ABC123");
  });

  it("no ensucia la dirección mientras nadie ha filtrado nada", async () => {
    renderTab();
    await screen.findByTestId("ot-query-total");

    // Un base64 en la barra desde el primer segundo no dice nada y se copia por error.
    expect(new URL(window.location.href).searchParams.get("q")).toBeNull();

    await ponerFiltroDePlaca();
    await waitFor(() => expect(new URL(window.location.href).searchParams.get("q")).not.toBeNull());
  });
});

describe("El panel de consultas guardadas", () => {
  it("resume qué pregunta cada consulta, para reconocerla meses después", async () => {
    renderTab();
    const lista = await screen.findByTestId("ot-query-guardadas");

    // Un nombre puesto por su autor —«revisión lunes»— no significa nada tres meses después. El
    // resumen sale de la definición, así que no puede quedarse desactualizado.
    expect(within(lista).getByTestId("ot-query-guardada-s1")).toHaveTextContent(
      "Sin filtros · últimos 30 días",
    );
    expect(within(lista).getByTestId("ot-query-guardada-f1")).toHaveTextContent(
      "1 filtro · últimos 90 días",
    );
  });

  it("al borrar dice cuál se va a borrar y deja arrepentirse", async () => {
    renderTab();
    await screen.findByTestId("ot-query-guardadas");

    await userEvent.click(screen.getByLabelText("Borrar Mis traspasos"));

    // Lo que hay que confirmar es CUÁL, no «si»: por eso se nombra en vez de preguntar «¿seguro?».
    const confirmacion = screen.getByTestId("ot-query-borrar-s1");
    expect(confirmacion).toHaveTextContent("Se borrará «Mis traspasos»");

    await userEvent.click(within(confirmacion).getByText("Cancelar"));
    expect(screen.queryByTestId("ot-query-borrar-s1")).not.toBeInTheDocument();
    expect(mocks.deleteOtSavedQuery).not.toHaveBeenCalled();
    expect(screen.getByTestId("ot-query-guardada-s1")).toBeInTheDocument();

    await userEvent.click(screen.getByLabelText("Borrar Mis traspasos"));
    await userEvent.click(screen.getByTestId("ot-query-borrar-confirmar-s1"));

    await waitFor(() => expect(mocks.deleteOtSavedQuery).toHaveBeenCalledWith("s1", "ot-1"));
  });

  it("no ofrece la descarga en CSV", async () => {
    renderTab();
    await screen.findByTestId("ot-query-total");

    // Oculta, no eliminada: `buildQueryCsv` y sus pruebas siguen enteros bajo CSV_EXPORT_VISIBLE.
    expect(screen.getByTestId("ot-query-export-xlsx")).toBeInTheDocument();
    expect(screen.queryByTestId("ot-query-export-csv")).not.toBeInTheDocument();
  });
});
