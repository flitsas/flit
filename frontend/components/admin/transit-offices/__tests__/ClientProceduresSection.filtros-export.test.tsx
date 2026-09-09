// HU #12218 / #12219 / #12220 — la bandeja del organismo estrena la misma barra de filtros que el
// listado del gestor, orden por subcampo y descarga a Excel.
//
// Lo que se fija aquí es el CONTRATO de cada una: qué controles hay (y cuál desapareció), qué viaja
// al servidor cuando se filtra, por qué se puede ordenar una celda que muestra dos datos, y que el
// archivo recorre TODO el universo filtrado en vez de las filas ya pintadas.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import {
  otProceduresExportFields,
  nombreArchivoBandejaOt,
} from "@/lib/admin/ot-procedures-export";
import { DEFAULT_OT_PROCEDURES_VISIBLE_COLUMNS } from "@/lib/admin/ot-procedures-columns";

const mocks = vi.hoisted(() => ({ download: vi.fn() }));

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedures: vi.fn(),
  searchOtClientProcedures: vi.fn(),
  fetchOtBandejaFilterFields: vi.fn(),
  fetchOtBandejaHealth: vi.fn(),
  fetchOtBandejaCounters: vi.fn(),
  fetchOtProfile: vi.fn(),
  approveOtClientProcedure: vi.fn(),
  rejectOtClientProcedure: vi.fn(),
  revokeOtClientProcedure: vi.fn(),
  generarOtConsolidadoMaestro: vi.fn(),
  fetchOtDocuments: vi.fn(),
  fetchOtAttachmentPreviewUrl: vi.fn(),
  adjuntarOtLicenciaTransito: vi.fn(),
}));

vi.mock("@/lib/api/admin-mandate-signers", () => ({ fetchMandateSigners: vi.fn() }));
vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: { listPublishedProcedureTypes: vi.fn().mockResolvedValue([]), analyzeDocument: vi.fn() },
}));
vi.mock("@/lib/api/admin-plate-ranges", () => ({
  assignPlateToProcedure: vi.fn(),
  listPlateDetails: vi.fn().mockResolvedValue([]),
  revokeProcedurePlate: vi.fn(),
  updateProcedurePlate: vi.fn(),
}));

// Solo se sustituye `download`: `exportarPorLotes` y `buildWorkbook` corren de verdad, que es lo
// que permite afirmar sobre el recorrido real y no sobre una promesa mockeada.
vi.mock("@/components/consultas/export", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/components/consultas/export")>()),
  download: mocks.download,
}));

vi.mock("@/lib/api/ui-preferences", () => ({
  uiPreferencesClient: {
    get: vi.fn().mockResolvedValue({ scope: "ot.procedures.columns", value: {} }),
    put: vi.fn().mockResolvedValue({ scope: "ot.procedures.columns", value: { visible: [] } }),
  },
}));

import {
  fetchOtBandejaCounters,
  fetchOtBandejaFilterFields,
  fetchOtBandejaHealth,
  fetchOtProfile,
  searchOtClientProcedures,
} from "@/lib/api/admin-ot";
import { ClientProceduresSection } from "../ClientProceduresSection";

const OT_ID = "aaaaaaaa-0001-4000-8000-000000000001";

function tramite(i: number, extra: Partial<OtClientProcedure> = {}): OtClientProcedure {
  const num = String(i).padStart(4, "0");
  return {
    id: `proc-${num}`,
    clientTenantId: "ten-a",
    clientTenantName: "Flota Andina S.A.S.",
    procedureTypeId: "tipo-1",
    procedureTypeName: "Matrícula inicial",
    referenceNumber: `RAD-${num}`,
    status: "entregado",
    placa: `P${num}`,
    vin: `VIN-${num}`,
    vendedorNombre: `Vendedor ${num}`,
    compradorNombre: `Comprador ${num}`,
    gestorNombre: `Gestor ${num}`,
    createdAt: "2026-06-18T03:00:00Z",
    ...extra,
  } as OtClientProcedure;
}

function renderSection() {
  return render(
    <ToastProvider>
      <ClientProceduresSection transitOfficeId={OT_ID} />
    </ToastProvider>,
  );
}

/** Una página cualquiera con `total` propio: es de ahí de donde el export saca cuántas quedan. */
function pagina(filas: OtClientProcedure[], totalCount: number, page = 1, pageSize = 20) {
  return { data: filas, totalCount, page, pageSize };
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(fetchOtProfile).mockResolvedValue({
    operationMode: "dashboard",
    quipuxReadOnly: false,
    transitOfficeId: OT_ID,
    featureFlags: [],
  });
  vi.mocked(fetchOtBandejaFilterFields).mockResolvedValue([]);
  vi.mocked(fetchOtBandejaHealth).mockResolvedValue({
    transitOfficeResolved: true,
    transitOfficeId: OT_ID,
    deliveredTotal: 0,
    deliveredWithGrant: 0,
    deliveredWithoutGrant: 0,
    hasDeliveredWithoutGrant: false,
  });
  vi.mocked(fetchOtBandejaCounters).mockResolvedValue({
    transitOfficeResolved: true,
    sinAsignarPlaca: 0,
    conPlacaAsignada: 0,
    aprobados: 0,
    rechazados: 0,
    sinGestion: 0,
    revocados: 0,
  });
  vi.mocked(searchOtClientProcedures).mockResolvedValue(pagina([tramite(1)], 1));
});

describe("Bandeja OT — la barra de filtros del listado de trámites (HU #12218)", () => {
  it("AC1 — trae búsqueda, Periodo, Filtros, Columnas y Exportar, y retira «Búsqueda avanzada»", async () => {
    renderSection();
    await screen.findByText("RAD-0001");

    expect(screen.getByLabelText("Buscar en la bandeja de trámites")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Periodo/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Filtros/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Columnas/ })).toBeInTheDocument();
    expect(screen.getByTestId("ot-bandeja-export-xlsx")).toBeInTheDocument();

    // El formulario desplegable que ocupaba media pantalla en reposo ya no existe.
    expect(screen.queryByRole("button", { name: /Búsqueda avanzada/ })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Filtrar por VIN/)).not.toBeInTheDocument();
  });

  it("AC4 — el borrador no mueve la bandeja hasta pulsar Aplicar", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("RAD-0001");
    const llamadasAntes = vi.mocked(searchOtClientProcedures).mock.calls.length;

    await user.type(screen.getByLabelText("Buscar en la bandeja de trámites"), "ABC");
    expect(vi.mocked(searchOtClientProcedures).mock.calls.length).toBe(llamadasAntes);

    await user.click(screen.getByRole("button", { name: /^Filtros/ }));
    await user.click(screen.getByRole("button", { name: /^Aplicar$/ }));

    await waitFor(() =>
      expect(searchOtClientProcedures).toHaveBeenCalledWith(
        expect.objectContaining({ busqueda: "ABC" }),
        expect.anything(),
        { transitOfficeId: OT_ID },
      ),
    );
  });

  it("AC3 — un chip dice qué está filtrando y quitarlo recarga sin ese filtro", async () => {
    const user = userEvent.setup();
    vi.mocked(fetchOtBandejaFilterFields).mockResolvedValue([
      {
        id: "placa",
        label: "Placa",
        kind: "texto",
        group: "Vehículo",
        operators: ["es_alguno", "contiene"],
        options: [],
        hint: null,
        admiteLista: true,
      },
    ]);
    renderSection();
    await screen.findByText("RAD-0001");

    await user.click(screen.getByRole("button", { name: /^Filtros/ }));
    await user.click(
      within(screen.getByTestId("ot-bandeja-filtros-campos")).getByRole("button", { name: "Placa" }),
    );
    await user.type(screen.getByRole("textbox"), "ABC123");
    await user.click(screen.getByTestId("ot-bandeja-filtros-aplicar-placa"));
    await user.click(screen.getByRole("button", { name: /^Aplicar$/ }));

    const chip = await screen.findByRole("button", { name: /Quitar filtro/ });
    expect(chip).toBeInTheDocument();

    await user.click(chip);
    await waitFor(() => {
      const ultima = vi.mocked(searchOtClientProcedures).mock.calls.at(-1)?.[0];
      expect(ultima?.condiciones).toBeUndefined();
    });
  });

  it("AC7 — apagar una columna la retira de la tabla", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("RAD-0001");
    expect(screen.getByText("VIN-0001")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Columnas/ }));
    await user.click(await screen.findByRole("checkbox", { name: "VIN" }));

    await waitFor(() => expect(screen.queryByText("VIN-0001")).not.toBeInTheDocument());
    // La bandeja sigue ahí: ocultar una columna no vacía la tabla.
    expect(screen.getByText("RAD-0001")).toBeInTheDocument();
  });

  it("AC8 — si el catálogo no carga, la bandeja se pinta igual y el panel ofrece reintentar", async () => {
    const user = userEvent.setup();
    vi.mocked(fetchOtBandejaFilterFields).mockRejectedValue(new Error("catálogo caído"));
    renderSection();

    expect(await screen.findByText("RAD-0001")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /^Filtros/ }));
    expect(screen.getByText(/No se pudieron cargar los filtros disponibles/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reintentar" })).toBeInTheDocument();
  });
});

describe("Bandeja OT — orden por subcampo (HU #12219)", () => {
  it("la cabecera de «Empresa / Gestor» ofrece las dos y ordena por la elegida", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("RAD-0001");

    await user.click(screen.getByRole("button", { name: /Ordenar Empresa \/ Gestor/ }));
    const menu = screen.getByRole("menu", { name: /Ordenar por, en Empresa \/ Gestor/ });
    expect(within(menu).getByText("Empresa cliente")).toBeInTheDocument();
    expect(within(menu).getByText("Gestor")).toBeInTheDocument();

    await user.click(within(menu).getByRole("menuitemradio", { name: "Empresa cliente: A-Z" }));

    await waitFor(() =>
      expect(searchOtClientProcedures).toHaveBeenCalledWith(
        expect.objectContaining({ sortBy: "empresa", sortDir: "asc" }),
        expect.anything(),
        { transitOfficeId: OT_ID },
      ),
    );
  });
});

describe("Bandeja OT — descarga a Excel (HU #12220)", () => {
  /**
   * El contrato del desapilado, probado sobre la función pura: lo que hay que fijar es QUÉ datos
   * salen y en cuántas columnas, no el HTML que los rodea.
   */
  it("AC4 — los datos apilados en una celda salen cada uno en su columna", () => {
    const ids = otProceduresExportFields(DEFAULT_OT_PROCEDURES_VISIBLE_COLUMNS).map((c) => c.id);

    // «Empresa / Gestor» es UNA celda en pantalla y DOS columnas en la hoja.
    expect(ids).toContain("empresa");
    expect(ids).toContain("gestor");
    // Lo mismo con el estado y su ruta de placa.
    expect(ids).toContain("estado");
    expect(ids).toContain("rutaPlaca");
    // Sin repeticiones: una columna aparece una sola vez aunque dos celdas la aportaran.
    expect(new Set(ids).size).toBe(ids.length);
  });

  it("AC4 — apagar una columna la saca del archivo", () => {
    const sinVin = otProceduresExportFields(
      DEFAULT_OT_PROCEDURES_VISIBLE_COLUMNS.filter((k) => k !== "vin"),
    ).map((c) => c.id);

    expect(sinVin).not.toContain("vin");
    expect(sinVin).toContain("placa");
  });

  it("AC6/AC7 — el nombre lleva fecha y hora, y solo numera cuando hay más de un archivo", () => {
    expect(nombreArchivoBandejaOt("2026-09-09_10-30")).toBe("bandeja-ot_2026-09-09_10-30.xlsx");
    expect(nombreArchivoBandejaOt("2026-09-09_10-30", { numero: 1, total: 1 })).toBe(
      "bandeja-ot_2026-09-09_10-30.xlsx",
    );
    expect(nombreArchivoBandejaOt("2026-09-09_10-30", { numero: 2, total: 3 })).toBe(
      "bandeja-ot_2026-09-09_10-30_parte_2_de_3.xlsx",
    );
  });

  /**
   * AC2 — lo que de verdad importa: el archivo NO sale de las filas pintadas. La bandeja muestra
   * una página de 20 y el universo es de 150, así que el recorrido tiene que pedir las páginas que
   * faltan hasta agotar el total.
   */
  it("AC2 — recorre todo el universo filtrado, no la página a la vista", async () => {
    const user = userEvent.setup();
    const universo = 150;
    vi.mocked(searchOtClientProcedures).mockImplementation(async (params) => {
      const page = params?.page ?? 1;
      const pageSize = params?.pageSize ?? 20;
      const desde = (page - 1) * pageSize;
      const filas = Array.from(
        { length: Math.max(0, Math.min(pageSize, universo - desde)) },
        (_, i) => tramite(desde + i + 1),
      );
      return pagina(filas, universo, page, pageSize);
    });

    renderSection();
    await screen.findByText("RAD-0001");
    mocks.download.mockClear();

    await user.click(screen.getByTestId("ot-bandeja-export-xlsx"));

    await waitFor(() => expect(mocks.download).toHaveBeenCalled());
    expect(await screen.findByText(/Se exportaron 150 trámites/)).toBeInTheDocument();

    // 150 filas caben en un solo archivo (el lote es de 5.000).
    expect(mocks.download).toHaveBeenCalledTimes(1);
    expect(mocks.download.mock.calls[0][1]).toMatch(/^bandeja-ot_.*\.xlsx$/);
  });

  it("AC8 — sin trámites que exportar, la acción está deshabilitada", async () => {
    vi.mocked(searchOtClientProcedures).mockResolvedValue(pagina([], 0));
    renderSection();

    await waitFor(() => expect(screen.getByTestId("ot-bandeja-export-xlsx")).toBeDisabled());
    expect(mocks.download).not.toHaveBeenCalled();
  });

  /** El recorrido usa los MISMOS filtros que la tabla: el archivo no puede traer lo que no se ve. */
  it("AC2 — el recorrido lleva los filtros aplicados", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("RAD-0001");

    await user.type(screen.getByLabelText("Buscar en la bandeja de trámites"), "ABC");
    await user.click(screen.getByRole("button", { name: /^Filtros/ }));
    await user.click(screen.getByRole("button", { name: /^Aplicar$/ }));
    await waitFor(() =>
      expect(searchOtClientProcedures).toHaveBeenCalledWith(
        expect.objectContaining({ busqueda: "ABC" }),
        expect.anything(),
        expect.anything(),
      ),
    );

    await user.click(screen.getByTestId("ot-bandeja-export-xlsx"));

    await waitFor(() =>
      expect(searchOtClientProcedures).toHaveBeenCalledWith(
        expect.objectContaining({ busqueda: "ABC", pageSize: 100 }),
        undefined,
        { transitOfficeId: OT_ID },
      ),
    );
  });
});
