// HU #13394 (épica #13216, Feature #13308) — «Descargar ZIP» en la barra de selección de la bandeja
// del OT: confirmación con el texto del maestro (CF-11), creación del lote por
// `POST /api/v1/admin/ot/consolidados/lotes` (mock del contrato de #13391), seguimiento en el aviso
// global (#13382), SuperAdmin con ?transitOfficeId (CF-10), 409 / 422 / 403 / 503 / red y sin
// permiso sin botón.
//
// Uso de ejemplo:
//   <ClientProceduresSection transitOfficeId={OT_ID} />   // token con consolidado-masivo.download
//   → marcar filas → «Descargar ZIP» → «Confirmar descarga»
//   → crearLoteConsolidadosOt({ tipoDocumento: "consolidado_maestro", confirmaEfectos: true, seleccion },
//       undefined, { transitOfficeId: OT_ID })  y  mostrarLote(lote)
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import type { LoteConsolidados } from "@/lib/api/types-consolidado-lotes";
import { ConsolidadoLotesApiError } from "@/lib/api/consolidado-lotes-client";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

const tracker = vi.hoisted(() => ({ mostrarLote: vi.fn() }));
vi.mock("@/components/shared/LoteDescargaTracker", () => ({
  useMostrarLoteDescarga: () => ({ mostrarLote: tracker.mostrarLote }),
}));

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
  crearLoteConsolidadosOt: vi.fn(),
}));
vi.mock("@/lib/api/admin-mandate-signers", () => ({ fetchMandateSigners: vi.fn() }));
vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: { listPublishedProcedureTypes: vi.fn().mockResolvedValue([]), analyzeDocument: vi.fn() },
  apiUrl: (path: string) => new URL(path, "http://localhost:3000").toString(),
  tenantHeader: () => ({}),
}));
vi.mock("@/lib/api/admin-plate-ranges", () => ({
  assignPlateToProcedure: vi.fn(),
  releaseProcedurePlate: vi.fn(),
  updateProcedurePlate: vi.fn(),
}));
vi.mock("@/lib/api/ui-preferences", () => ({
  uiPreferencesClient: {
    get: vi.fn().mockResolvedValue({ scope: "ot.procedures.columns", value: {} }),
    put: vi.fn().mockResolvedValue({ scope: "ot.procedures.columns", value: { visible: [] } }),
  },
}));

import {
  crearLoteConsolidadosOt,
  fetchOtBandejaFilterFields,
  fetchOtBandejaHealth,
  fetchOtProfile,
  searchOtBandejaCounters,
  searchOtClientProcedures,
} from "@/lib/api/admin-ot";
import { ClientProceduresSection } from "../ClientProceduresSection";

const OT_ID = "aaaaaaaa-0001-4000-8000-000000000001";
const OTRO_OT_ID = "bbbbbbbb-0002-4000-8000-000000000002";
const LOTE_ACTIVO_ID = "0f8c6a1e-0000-4000-8000-000000000099";
const TEXTO_MAESTRO =
  "Se descargará el consolidado maestro que cada trámite tiene guardado, tal como está, aunque no refleje cambios posteriores. Solo se generará el consolidado maestro de los trámites que todavía no tienen uno.";

/** Lote sintético según `LoteConsolidados` del contrato. */
const LOTE: LoteConsolidados = {
  id: "0f8c6a1e-0000-4000-8000-000000000001",
  estado: "en_cola",
  tipoDocumento: "consolidado_maestro",
  total: 119,
  procesados: 0,
  incluidos: 0,
  omitidos: 0,
  creadoEn: "2026-10-07T15:00:00Z",
  partes: [],
};

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

/** Trámites sintéticos, sin PII. */
function tramite(i: number): OtClientProcedure {
  const num = String(i).padStart(4, "0");
  return {
    id: `proc-${num}`,
    clientTenantId: "ten-a",
    clientTenantName: "Compañía de prueba",
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
  } as OtClientProcedure;
}

function servidorConTotal(total: number) {
  vi.mocked(searchOtClientProcedures).mockImplementation(async (params) => {
    const page = params?.page ?? 1;
    const size = params?.pageSize ?? 10;
    const desde = (page - 1) * size;
    const hasta = Math.min(total, desde + size);
    const data = Array.from({ length: Math.max(0, hasta - desde) }, (_, k) => tramite(desde + k + 1));
    return { data, totalCount: total, page, pageSize: size };
  });
}

const conToken = (payload: Record<string, unknown>) =>
  window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken(payload));
const otAdminConPermiso = () =>
  conToken({ sub: "u-ot", role_code: "ot_admin", permissions: ["consolidado-masivo.download"] });

const renderSection = (transitOfficeId = OT_ID) =>
  render(
    <ToastProvider>
      <ClientProceduresSection transitOfficeId={transitOfficeId} />
    </ToastProvider>,
  );

const contador = () => screen.getByTestId("contador-seleccion-lote");
const casillaTodos = () => screen.getByRole("checkbox", { name: /Seleccionar todos/ });
const casillaDe = (radicado: string) =>
  screen.getByRole("checkbox", { name: new RegExp(`trámite ${radicado} para la descarga masiva`) });
const botonZip = () => screen.getByRole("button", { name: /Descargar ZIP/ });
const dialogo = () => screen.getByRole("dialog", { name: /Descargar consolidados/ });
const confirmar = () => within(dialogo()).getByRole("button", { name: /Confirmar descarga/ });

/** Marca una fila y abre la confirmación. */
async function abrirConfirmacion(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByText("RAD-0001");
  await user.click(casillaDe("RAD-0001"));
  await user.click(botonZip());
  return dialogo();
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));
  vi.mocked(fetchOtProfile).mockResolvedValue({
    operationMode: "dashboard",
    quipuxReadOnly: false,
    transitOfficeId: OT_ID,
    featureFlags: [],
    revocationWindowBusinessDays: null,
  });
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
  servidorConTotal(3);
});

afterEach(() => {
  vi.unstubAllGlobals();
  window.localStorage.removeItem(TOKEN_STORAGE_KEY);
});

describe("Bandeja OT — «Descargar ZIP» de maestros (HU #13394)", { timeout: 30_000 }, () => {
  it("AC1 — abre la confirmación con el texto del maestro, sin selector de tipo; foco atrapado y Escape cierra sin crear", async () => {
    const user = userEvent.setup();
    otAdminConPermiso();
    renderSection();
    const d = await abrirConfirmacion(user);

    expect(within(d).getByText(TEXTO_MAESTRO)).toBeInTheDocument();
    expect(within(d).queryByRole("combobox")).not.toBeInTheDocument();
    expect(within(d).queryByRole("radio")).not.toBeInTheDocument();
    expect(d.contains(document.activeElement)).toBe(true);
    for (let i = 0; i < 4; i++) {
      await user.tab();
      expect(d.contains(document.activeElement)).toBe(true);
    }

    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(crearLoteConsolidadosOt).not.toHaveBeenCalled();
    expect(contador()).toHaveTextContent("1 trámite seleccionado");
  });

  it("AC2 — confirmar con «todos» del filtro (lista pegada) envía el filtro sin orden, cierra, limpia y muestra el lote", async () => {
    const user = userEvent.setup();
    otAdminConPermiso();
    servidorConTotal(120);
    vi.mocked(crearLoteConsolidadosOt).mockResolvedValue(LOTE);
    renderSection();
    await screen.findByText("RAD-0001");

    await user.click(screen.getByRole("button", { name: /^Filtros/ }));
    await user.click(
      within(screen.getByTestId("ot-bandeja-filtros-campos")).getByRole("button", { name: "Placa" }),
    );
    await user.type(screen.getByRole("textbox"), "AAA111,BBB222");
    await user.click(screen.getByTestId("ot-bandeja-filtros-aplicar-placa"));
    await user.click(screen.getByRole("button", { name: /^Aplicar$/ }));
    await waitFor(() =>
      expect(vi.mocked(searchOtClientProcedures).mock.calls.at(-1)?.[0]?.condiciones).toBeDefined(),
    );
    await screen.findByText("RAD-0001");

    await user.click(casillaTodos());
    await user.click(casillaDe("RAD-0001"));
    expect(contador()).toHaveTextContent("119 trámites seleccionados");

    await user.click(botonZip());
    await user.click(confirmar());

    await waitFor(() => expect(tracker.mostrarLote).toHaveBeenCalledWith(LOTE));
    expect(crearLoteConsolidadosOt).toHaveBeenCalledTimes(1);
    const [cuerpo, , alcance] = vi.mocked(crearLoteConsolidadosOt).mock.calls[0];
    expect(cuerpo).toEqual({
      tipoDocumento: "consolidado_maestro",
      confirmaEfectos: true,
      seleccion: {
        modo: "filtro",
        ids: [],
        excluidos: ["proc-0001"],
        filtro: expect.objectContaining({
          condiciones: [expect.objectContaining({ fieldId: "placa", values: ["AAA111", "BBB222"] })],
        }),
      },
    });
    expect(cuerpo.seleccion.filtro).not.toHaveProperty("sortBy");
    expect(cuerpo.seleccion.filtro).not.toHaveProperty("sortDir");
    expect(alcance).toEqual({ transitOfficeId: OT_ID });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(contador()).toHaveTextContent("0 trámites seleccionados");
  });

  it("AC3 — SuperAdmin en la bandeja del organismo X: la petición lleva transitOfficeId = X", async () => {
    const user = userEvent.setup();
    conToken({ sub: "u-sa", role_code: "SuperAdmin" });
    vi.mocked(crearLoteConsolidadosOt).mockResolvedValue(LOTE);
    renderSection(OTRO_OT_ID);
    await abrirConfirmacion(user);
    await user.click(confirmar());

    await waitFor(() => expect(crearLoteConsolidadosOt).toHaveBeenCalled());
    expect(vi.mocked(crearLoteConsolidadosOt).mock.calls[0][2]).toEqual({ transitOfficeId: OTRO_OT_ID });
    expect(vi.mocked(crearLoteConsolidadosOt).mock.calls[0][0].seleccion).toEqual({
      modo: "ids",
      ids: ["proc-0001"],
      excluidos: [],
      filtro: null,
    });
  });

  it("AC4 — 409 lote_activo: el tracker muestra el lote activo, aviso de descarga en curso y la selección se conserva", async () => {
    const user = userEvent.setup();
    otAdminConPermiso();
    vi.mocked(crearLoteConsolidadosOt).mockRejectedValue(
      new ConsolidadoLotesApiError(409, "lote_activo", LOTE_ACTIVO_ID),
    );
    renderSection();
    await abrirConfirmacion(user);
    await user.click(confirmar());

    await waitFor(() => expect(tracker.mostrarLote).toHaveBeenCalledWith(LOTE_ACTIVO_ID));
    expect(await within(dialogo()).findByText(/Ya hay una descarga en curso/)).toBeInTheDocument();
    await user.click(within(dialogo()).getByRole("button", { name: "Entendido" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(contador()).toHaveTextContent("1 trámite seleccionado");
    expect(casillaDe("RAD-0001")).toBeChecked();
  });

  it.each([
    ["422", new ConsolidadoLotesApiError(422, null), /tope de 10\.000/, true],
    ["503", new ConsolidadoLotesApiError(503, "auditoria_no_registrada"), /^No se pudo completar la descarga, intente de nuevo$/, true],
    ["red", new ConsolidadoLotesApiError(0, null), /^No se pudo completar la descarga, intente de nuevo$/, true],
    ["403", new ConsolidadoLotesApiError(403, null), /No tienes permiso/, false],
  ])("AC5 — %s: mensaje correcto, la selección se conserva y solo se reintenta si aplica", async (_n, error, mensaje, reintenta) => {
    const user = userEvent.setup();
    otAdminConPermiso();
    vi.mocked(crearLoteConsolidadosOt).mockRejectedValue(error);
    renderSection();
    await abrirConfirmacion(user);
    await user.click(confirmar());

    expect(await within(dialogo()).findByRole("alert")).toHaveTextContent(mensaje);
    if (reintenta) expect(confirmar()).toBeEnabled();
    else expect(confirmar()).toBeDisabled();
    expect(crearLoteConsolidadosOt).toHaveBeenCalledTimes(1);
    expect(tracker.mostrarLote).not.toHaveBeenCalled();

    await user.click(within(dialogo()).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(contador()).toHaveTextContent("1 trámite seleccionado");
  });

  it("AC6 — sin consolidado-masivo.download no se muestra «Descargar ZIP»", async () => {
    conToken({ sub: "u-ot", role_code: "ot_admin", permissions: ["ot.bandeja.read"] });
    renderSection();
    await screen.findByText("RAD-0001");
    expect(screen.queryByRole("button", { name: /Descargar ZIP/ })).not.toBeInTheDocument();
    // Tampoco hay casillas con las que llegar a él: la acción no existe para este usuario.
    expect(screen.queryByRole("checkbox", { name: /para la descarga masiva/ })).not.toBeInTheDocument();
    expect(crearLoteConsolidadosOt).not.toHaveBeenCalled();
  });

  it("AC6 — con permiso pero sin selección tampoco hay botón (aparece al marcar)", async () => {
    const user = userEvent.setup();
    otAdminConPermiso();
    renderSection();
    await screen.findByText("RAD-0001");
    expect(screen.queryByRole("button", { name: /Descargar ZIP/ })).not.toBeInTheDocument();
    await user.click(casillaDe("RAD-0001"));
    expect(botonZip()).toBeInTheDocument();
  });
});
