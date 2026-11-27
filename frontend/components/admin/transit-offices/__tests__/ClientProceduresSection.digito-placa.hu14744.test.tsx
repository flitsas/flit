// HU #12218 / #12219 / #12220 — la bandeja del organismo estrena la misma barra de filtros que el
// listado del gestor, orden por subcampo y descarga a Excel.
//
// Lo que se fija aquí es el CONTRATO de cada una: qué controles hay (y cuál desapareció), qué viaja
// al servidor cuando se filtra, por qué se puede ordenar una celda que muestra dos datos, y que el
// archivo recorre TODO el universo filtrado en vez de las filas ya pintadas.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import { DEFAULT_OT_PROCEDURES_VISIBLE_COLUMNS, OT_PROCEDURES_COLUMNS } from "@/lib/admin/ot-procedures-columns";

const mocks = vi.hoisted(() => ({ download: vi.fn() }));

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedures: vi.fn(),
  searchOtClientProcedures: vi.fn(),
  fetchOtBandejaFilterFields: vi.fn(),
  fetchOtBandejaHealth: vi.fn(),
  searchOtBandejaCounters: vi.fn(),
  fetchOtProfile: vi.fn(),
  approveOtClientProcedure: vi.fn(),
  rejectOtClientProcedure: vi.fn(),
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
  releaseProcedurePlate: vi.fn(),
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
    get: vi.fn(),
    put: vi.fn().mockResolvedValue({ scope: "ot.procedures.columns", value: { visible: [] } }),
  },
}));

import {
  searchOtBandejaCounters,
  fetchOtBandejaFilterFields,
  fetchOtBandejaHealth,
  fetchOtProfile,
  searchOtClientProcedures,
} from "@/lib/api/admin-ot";
import { uiPreferencesClient } from "@/lib/api/ui-preferences";
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
    revocationWindowBusinessDays: null,
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
  vi.mocked(searchOtBandejaCounters).mockResolvedValue({
    transitOfficeResolved: true,
    preasignacion: 0,
    asignados: 0,
    porDecidir: 0,
    aprobados: 0,
    rechazados: 0,
    revocados: 0,
    solicitudesRevocatoria: 0,
  });
  vi.mocked(searchOtClientProcedures).mockImplementation(async (params) => {
    const filas = [
      tramite(1, { familia: "MATRICULAS", platePreferredLastDigit: "7", platePreferredLastDigitDeclared: true, clase: "MOTOCICLETA", placaRutaCorta: false }),
      tramite(2, { familia: "MATRICULAS", platePreferredLastDigit: "3", platePreferredLastDigitDeclared: true, clase: null, placaRutaCorta: false }),
      tramite(3, { familia: "MATRICULAS", platePreferredLastDigit: "", platePreferredLastDigitDeclared: true, placaRutaCorta: false }),
      tramite(4, { familia: "MATRICULAS", platePreferredLastDigit: "3", platePreferredLastDigitDeclared: true, placaRutaCorta: true }),
      tramite(5, { familia: "TRASPASO", platePreferredLastDigit: "9", platePreferredLastDigitDeclared: true }),
    ].filter((f) => !params?.familia || f.familia === params.familia);
    return pagina(filas, filas.length);
  });
});


const conColumna = () =>
  vi.mocked(uiPreferencesClient.get).mockResolvedValue({
    scope: "ot.procedures.columns",
    value: {
      visible: [...DEFAULT_OT_PROCEDURES_VISIBLE_COLUMNS, "digitoPlaca"],
      known: OT_PROCEDURES_COLUMNS.map((c) => c.key),
    },
  } as never);
const cabecera = () => screen.queryByRole("columnheader", { name: /Dígito de placa/ });

describe("Bandeja OT — columna «Dígito de placa» (HU #14744)", () => {
  it("AC6: no viene por defecto, ni en Todos ni en Matrículas", async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({ scope: "ot.procedures.columns", value: {} } as never);
    renderSection();
    await screen.findByText("RAD-0001");
    expect(cabecera()).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("tab", { name: "Matrículas" }));
    await screen.findByText("RAD-0001");
    expect(cabecera()).not.toBeInTheDocument();
  });

  it("AC1-AC4: en Matrículas, con la columna marcada, cada fila dice lo que corresponde", async () => {
    conColumna();
    renderSection();
    await screen.findByText("RAD-0001");
    await userEvent.click(screen.getByRole("tab", { name: "Matrículas" }));
    expect(await screen.findByRole("columnheader", { name: /Dígito de placa/ })).toBeInTheDocument();
    expect(screen.getAllByTestId("ot-digito-placa").map((c) => c.textContent)).toEqual([
      "Inicia en 7",
      "Termina en 3",
      "Sin preferencia",
      "—",
    ]);
  });

  it("Todos: la columna se ofrece y, marcada, muestra el valor en matrículas y vacío en un traspaso", async () => {
    conColumna();
    renderSection();
    await screen.findByText("RAD-0001");
    expect(await screen.findByRole("columnheader", { name: /Dígito de placa/ })).toBeInTheDocument();
    expect(screen.getAllByTestId("ot-digito-placa").map((c) => c.textContent)).toEqual([
      "Inicia en 7",
      "Termina en 3",
      "Sin preferencia",
      "—",
      "",
    ]);
    await userEvent.click(screen.getByRole("button", { name: /Columnas/ }));
    expect(await screen.findByRole("checkbox", { name: /Dígito de placa/ })).toBeChecked();
  });

  it("AC5: desaparece en Traspaso (tabla y panel Columnas) y reaparece al volver", async () => {
    conColumna();
    renderSection();
    await screen.findByText("RAD-0001");

    await userEvent.click(screen.getByRole("tab", { name: "Matrículas" }));
    expect(await screen.findByRole("columnheader", { name: /Dígito de placa/ })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("tab", { name: "Traspaso" }));
    await screen.findByText("RAD-0005");
    expect(cabecera()).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /Columnas/ }));
    expect(await screen.findAllByRole("checkbox")).not.toHaveLength(0);
    expect(screen.queryByRole("checkbox", { name: /Dígito de placa/ })).not.toBeInTheDocument();
    await userEvent.keyboard("{Escape}");

    await userEvent.click(screen.getByRole("tab", { name: "Matrículas" }));
    expect(await screen.findByRole("columnheader", { name: /Dígito de placa/ })).toBeInTheDocument();
  });

  it("AC5/AC6: en Todos y en Matrículas el panel Columnas la ofrece sin marcar", async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({ scope: "ot.procedures.columns", value: {} } as never);
    renderSection();
    await screen.findByText("RAD-0001");
    await userEvent.click(screen.getByRole("button", { name: /Columnas/ }));
    expect(await screen.findByRole("checkbox", { name: /Dígito de placa/ })).not.toBeChecked();
    await userEvent.keyboard("{Escape}");
    await userEvent.click(screen.getByRole("tab", { name: "Matrículas" }));
    await userEvent.click(screen.getByRole("button", { name: /Columnas/ }));
    expect(await screen.findByRole("checkbox", { name: /Dígito de placa/ })).not.toBeChecked();
  });
});
