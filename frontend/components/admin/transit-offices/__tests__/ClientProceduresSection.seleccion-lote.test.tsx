// HU #13393 (épica #13216, Feature #13308) — selección de trámites en la bandeja del OT para la
// descarga masiva de consolidados maestros: casilla por fila, «Seleccionar todos» del filtro (con
// listas pegadas), contador exacto, reinicio al cambiar el contexto y sin permiso nada de eso.
//
// Uso de ejemplo:
//   <ClientProceduresSection transitOfficeId={OT_ID} />   // token con consolidado-masivo.download
//   → barra «Seleccionar todos (del filtro)» + contador y una casilla por fila de la bandeja.
//   <ClientProceduresTable rows={...} seleccionable seleccion={lote} onToggle={lote.alternar} ... />
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

// Tope reducido solo para AC6: llegar a 10.000 a mano en una prueba de UI no es viable, y el
// cálculo del tope ya lo fija `useSeleccionLote.test.ts`. Aquí se prueba que la bandeja lo respeta
// y muestra el mensaje.
const mocks = vi.hoisted(() => ({ tope: undefined as number | undefined, total: 0 }));

vi.mock("@/hooks/useSeleccionLote", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/hooks/useSeleccionLote")>();
  return {
    ...actual,
    useSeleccionLote: <T,>(o: Parameters<typeof actual.useSeleccionLote<T>>[0]) =>
      actual.useSeleccionLote<T>({ ...o, tope: mocks.tope ?? o.tope }),
  };
});

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
vi.mock("@/lib/api/ui-preferences", () => ({
  uiPreferencesClient: {
    get: vi.fn().mockResolvedValue({ scope: "ot.procedures.columns", value: {} }),
    put: vi.fn().mockResolvedValue({ scope: "ot.procedures.columns", value: { visible: [] } }),
  },
}));

import {
  fetchOtBandejaFilterFields,
  fetchOtBandejaHealth,
  fetchOtProfile,
  searchOtBandejaCounters,
  searchOtClientProcedures,
} from "@/lib/api/admin-ot";
import { ClientProceduresSection } from "../ClientProceduresSection";
import { ClientProceduresTable } from "../ClientProceduresTable";

const OT_ID = "aaaaaaaa-0001-4000-8000-000000000001";
const OTRO_OT_ID = "bbbbbbbb-0002-4000-8000-000000000002";

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

/** Doble del servidor: página pedida del universo de `mocks.total` trámites, con su totalCount. */
function servidorConTotal(total: number) {
  mocks.total = total;
  vi.mocked(searchOtClientProcedures).mockImplementation(async (params) => {
    const page = params?.page ?? 1;
    const size = params?.pageSize ?? 10;
    const desde = (page - 1) * size;
    const hasta = Math.min(mocks.total, desde + size);
    const data = Array.from({ length: Math.max(0, hasta - desde) }, (_, k) => tramite(desde + k + 1));
    return { data, totalCount: mocks.total, page, pageSize: size };
  });
}

function conToken(payload: Record<string, unknown>) {
  window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken(payload));
}
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

beforeEach(() => {
  vi.clearAllMocks();
  mocks.tope = undefined;
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
  window.localStorage.removeItem(TOKEN_STORAGE_KEY);
});

/** Pega una lista de placas en el filtro «Placa» y la aplica (lo que hace el OT desde Excel). */
async function pegarListaDePlacas(user: ReturnType<typeof userEvent.setup>, lista: string) {
  await user.click(screen.getByRole("button", { name: /^Filtros/ }));
  await user.click(
    within(screen.getByTestId("ot-bandeja-filtros-campos")).getByRole("button", { name: "Placa" }),
  );
  await user.type(screen.getByRole("textbox"), lista);
  await user.click(screen.getByTestId("ot-bandeja-filtros-aplicar-placa"));
  await user.click(screen.getByRole("button", { name: /^Aplicar$/ }));
}

describe("Bandeja OT — selección para la descarga masiva (HU #13393)", { timeout: 30_000 }, () => {
  it("AC1 — con permiso cada fila tiene casilla que nombra el radicado; clic y teclado no abren el detalle", async () => {
    const user = userEvent.setup();
    otAdminConPermiso();
    renderSection();
    await screen.findByText("RAD-0001");

    expect(screen.getAllByRole("checkbox", { name: /para la descarga masiva/ })).toHaveLength(3);
    await user.click(casillaDe("RAD-0001"));
    expect(casillaDe("RAD-0001")).toBeChecked();

    casillaDe("RAD-0002").focus();
    await user.keyboard(" ");
    expect(casillaDe("RAD-0002")).toBeChecked();
    await user.keyboard("{Enter}");

    expect(contador()).toHaveTextContent("2 trámites seleccionados");
    // El detalle del trámite es un diálogo: marcar no lo abrió.
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("AC2 — lista pegada con totalCount 120: «todos» cuenta 120 y desmarcar 2 da 118", async () => {
    const user = userEvent.setup();
    otAdminConPermiso();
    servidorConTotal(120);
    renderSection();
    await screen.findByText("RAD-0001");

    await pegarListaDePlacas(user, "AAA111,BBB222,CCC333");
    await waitFor(() =>
      expect(vi.mocked(searchOtClientProcedures).mock.calls.at(-1)?.[0]?.condiciones).toEqual([
        expect.objectContaining({ fieldId: "placa", values: ["AAA111", "BBB222", "CCC333"] }),
      ]),
    );
    await screen.findByText("RAD-0001");

    await user.click(casillaTodos());
    expect(contador()).toHaveTextContent("120 trámites seleccionados (todos los del filtro)");
    expect(casillaTodos()).toHaveAttribute("aria-checked", "true");
    expect(casillaDe("RAD-0005")).toBeChecked();

    await user.click(casillaDe("RAD-0001"));
    await user.click(casillaDe("RAD-0002"));
    expect(contador()).toHaveTextContent("118 trámites seleccionados");
    expect(casillaTodos()).toHaveAttribute("aria-checked", "mixed");
  });

  it("AC3 — 3 filas marcadas se conservan al cambiar de página y el contador sigue en 3", async () => {
    const user = userEvent.setup();
    otAdminConPermiso();
    servidorConTotal(23);
    renderSection();
    await screen.findByText("RAD-0001");

    for (const r of ["RAD-0001", "RAD-0002", "RAD-0003"]) await user.click(casillaDe(r));
    expect(contador()).toHaveTextContent("3 trámites seleccionados");

    await user.click(screen.getByRole("button", { name: "Página siguiente" }));
    await screen.findByText("RAD-0011");
    expect(contador()).toHaveTextContent("3 trámites seleccionados");
    expect(casillaDe("RAD-0011")).not.toBeChecked();

    await user.click(screen.getByRole("button", { name: "Página anterior" }));
    await screen.findByText("RAD-0001");
    expect(casillaDe("RAD-0002")).toBeChecked();
    expect(contador()).toHaveTextContent("3 trámites seleccionados");
  });

  it("AC4 — cambiar la búsqueda (filtro) reinicia la selección a 0", async () => {
    const user = userEvent.setup();
    otAdminConPermiso();
    renderSection();
    await screen.findByText("RAD-0001");
    await user.click(casillaTodos());
    expect(contador()).toHaveTextContent("3 trámites seleccionados");

    await user.type(screen.getByLabelText("Buscar en la bandeja de trámites"), "P0001");
    await waitFor(
      () =>
        expect(searchOtClientProcedures).toHaveBeenLastCalledWith(
          expect.objectContaining({ busqueda: "P0001" }),
          expect.anything(),
          { transitOfficeId: OT_ID },
        ),
      { timeout: 5_000 },
    );
    await waitFor(() => expect(contador()).toHaveTextContent("0 trámites seleccionados"));
    expect(casillaTodos()).not.toBeChecked();
  });

  it("AC4 — pegar una lista de placas reinicia la selección a 0", async () => {
    const user = userEvent.setup();
    otAdminConPermiso();
    renderSection();
    await screen.findByText("RAD-0001");
    await user.click(casillaDe("RAD-0001"));
    expect(contador()).toHaveTextContent("1 trámite seleccionado");

    await pegarListaDePlacas(user, "AAA111,BBB222");
    await waitFor(() => expect(contador()).toHaveTextContent("0 trámites seleccionados"));
  });

  it("AC4 — cambiar la pestaña de familia reinicia la selección a 0", async () => {
    const user = userEvent.setup();
    otAdminConPermiso();
    renderSection();
    await screen.findByText("RAD-0001");
    await user.click(casillaTodos());
    expect(contador()).toHaveTextContent("3 trámites seleccionados");

    await user.click(screen.getByRole("tab", { name: "Traspaso" }));
    await waitFor(() =>
      expect(vi.mocked(searchOtClientProcedures).mock.calls.at(-1)?.[0]?.familia).toBe("TRASPASO"),
    );
    await waitFor(() => expect(contador()).toHaveTextContent("0 trámites seleccionados"));
  });

  it("AC4 — cambiar de organismo reinicia la selección a 0; cambiar el orden no", async () => {
    const user = userEvent.setup();
    otAdminConPermiso();
    const { rerender } = renderSection();
    await screen.findByText("RAD-0001");
    await user.click(casillaDe("RAD-0001"));
    await user.click(casillaDe("RAD-0002"));
    expect(contador()).toHaveTextContent("2 trámites seleccionados");

    // Reordenar no cambia el universo: la selección sigue.
    await user.click(screen.getByRole("button", { name: /Ordenar por VIN/ }));
    await waitFor(() =>
      expect(vi.mocked(searchOtClientProcedures).mock.calls.at(-1)?.[0]?.sortBy).not.toBe("createdAt"),
    );
    await screen.findByText("RAD-0001");
    expect(contador()).toHaveTextContent("2 trámites seleccionados");

    rerender(
      <ToastProvider>
        <ClientProceduresSection transitOfficeId={OTRO_OT_ID} />
      </ToastProvider>,
    );
    await waitFor(() =>
      expect(searchOtClientProcedures).toHaveBeenLastCalledWith(expect.anything(), expect.anything(), {
        transitOfficeId: OTRO_OT_ID,
      }),
    );
    await waitFor(() => expect(contador()).toHaveTextContent("0 trámites seleccionados"));
  });

  it("AC5 — sin el permiso (o token emitido antes del grant) no hay casillas, cabecera ni contador", async () => {
    // Mismo ot_admin, pero su token no trae el slug: no se «arregla» en el cliente (FB-i).
    conToken({ sub: "u-ot", role_code: "ot_admin", permissions: ["ot.bandeja.read"] });
    renderSection();
    await screen.findByText("RAD-0001");

    expect(screen.queryByRole("region", { name: /descarga masiva de consolidados/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("checkbox", { name: /Seleccionar todos/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("checkbox", { name: /para la descarga masiva/ })).not.toBeInTheDocument();
    expect(screen.queryByTestId("contador-seleccion-lote")).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: /Selección para la descarga masiva/ })).not.toBeInTheDocument();
    // El resto de la bandeja sigue igual: filas, acciones y exportar.
    expect(screen.getByText("RAD-0002")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Acciones del trámite RAD-0001" })).toBeInTheDocument();
    expect(screen.getByTestId("ot-bandeja-export-xlsx")).toBeEnabled();
  });

  it("AC5 — sin sesión tampoco hay selección", async () => {
    renderSection();
    await screen.findByText("RAD-0001");
    expect(screen.queryByRole("checkbox", { name: /para la descarga masiva/ })).not.toBeInTheDocument();
  });

  it("AC6 — al llegar al tope no se admiten más filas y un mensaje sugiere usar el filtro", async () => {
    const user = userEvent.setup();
    mocks.tope = 2;
    otAdminConPermiso();
    renderSection();
    await screen.findByText("RAD-0001");

    await user.click(casillaDe("RAD-0001"));
    await user.click(casillaDe("RAD-0002"));
    await user.click(casillaDe("RAD-0003"));
    expect(casillaDe("RAD-0003")).not.toBeChecked();
    expect(contador()).toHaveTextContent("2 trámites seleccionados");
    expect(screen.getByText(/Usa el filtro y «Seleccionar todos»/)).toBeInTheDocument();
  });

  it("AC6 — con «todos», las exclusiones también topan y el mensaje sugiere ajustar el filtro", async () => {
    const user = userEvent.setup();
    mocks.tope = 1;
    otAdminConPermiso();
    renderSection();
    await screen.findByText("RAD-0001");

    await user.click(casillaTodos());
    await user.click(casillaDe("RAD-0001"));
    await user.click(casillaDe("RAD-0002"));
    expect(casillaDe("RAD-0002")).toBeChecked();
    expect(contador()).toHaveTextContent("2 trámites seleccionados");
    expect(screen.getByText(/Ajusta el filtro/)).toBeInTheDocument();
  });

  it("AC7 — cargando: «Seleccionar todos» deshabilitada y sin contador", async () => {
    otAdminConPermiso();
    vi.mocked(searchOtClientProcedures).mockReturnValue(new Promise(() => {}));
    renderSection();
    await waitFor(() => expect(casillaTodos()).toBeDisabled());
    expect(contador()).toBeEmptyDOMElement();
  });

  it("AC7 — bandeja vacía: «Seleccionar todos» deshabilitada y sin contador", async () => {
    otAdminConPermiso();
    servidorConTotal(0);
    renderSection();
    await screen.findByText("No hay trámites pendientes de tus clientes.");
    expect(casillaTodos()).toBeDisabled();
    expect(contador()).toBeEmptyDOMElement();
  });

  it("AC7 — error al cargar: «Seleccionar todos» deshabilitada y sin contador", async () => {
    otAdminConPermiso();
    vi.mocked(searchOtClientProcedures).mockRejectedValue(new Error("fallo de red"));
    renderSection();
    await screen.findByText("Error al cargar trámites de clientes.");
    expect(casillaTodos()).toBeDisabled();
    expect(contador()).toBeEmptyDOMElement();
  });

  it("AC7 — con datos la cabecera refleja nada, parcial (mixed) y todo", async () => {
    const user = userEvent.setup();
    otAdminConPermiso();
    renderSection();
    await screen.findByText("RAD-0001");

    expect(casillaTodos()).toBeEnabled();
    expect(casillaTodos()).toHaveAttribute("aria-checked", "false");
    await user.click(casillaDe("RAD-0001"));
    expect(casillaTodos()).toHaveAttribute("aria-checked", "mixed");
    await user.click(casillaDe("RAD-0002"));
    await user.click(casillaDe("RAD-0003"));
    expect(casillaTodos()).toHaveAttribute("aria-checked", "true");
    // Desde «todo», la cabecera limpia.
    await user.click(casillaTodos());
    expect(contador()).toHaveTextContent("0 trámites seleccionados");
  });

  it("AC8 — el contador es una región aria-live polite y las casillas tienen foco visible sin hex", async () => {
    otAdminConPermiso();
    renderSection();
    await screen.findByText("RAD-0001");

    expect(contador()).toHaveAttribute("role", "status");
    expect(contador()).toHaveAttribute("aria-live", "polite");
    for (const casilla of [casillaTodos(), casillaDe("RAD-0001")]) {
      expect(casilla.className).toMatch(/focus-visible:ring-2/);
      expect(casilla.className).not.toMatch(/#[0-9a-f]{3,8}/i);
    }
    const th = screen.getByRole("columnheader", { name: /Selección para la descarga masiva/ });
    expect(th.className).not.toMatch(/#[0-9a-f]{3,8}/i);
    const celda = casillaDe("RAD-0001").closest("td");
    expect(celda?.className).not.toMatch(/#[0-9a-f]{3,8}/i);
    expect(celda?.getAttribute("style") ?? "").not.toMatch(/#[0-9a-f]{3,8}/i);
  });
});

describe("ClientProceduresTable — columna de selección (HU #13393)", () => {
  const base = {
    rows: [tramite(1), tramite(2)],
    totalCount: 2,
    page: 1,
    pageSize: 10,
    onPageChange: vi.fn(),
    onApprove: vi.fn(),
    onReject: vi.fn(),
    sortBy: "createdAt",
    sortDir: "desc" as const,
    onSortChange: vi.fn(),
  };

  it("AC1 — clic, Espacio y Enter en la casilla no disparan onVerDetalle; la fila sí", async () => {
    const user = userEvent.setup();
    const onVerDetalle = vi.fn();
    const onToggle = vi.fn(() => true);
    render(
      <ClientProceduresTable
        {...base}
        onVerDetalle={onVerDetalle}
        seleccionable
        seleccion={{ estaSeleccionado: (id) => id === "proc-0002" }}
        onToggle={onToggle}
      />,
    );

    const casilla = screen.getByRole("checkbox", { name: /RAD-0001 para la descarga masiva/ });
    await user.click(casilla);
    casilla.focus();
    await user.keyboard(" ");
    await user.keyboard("{Enter}");
    expect(onToggle).toHaveBeenCalledWith("proc-0001");
    expect(onVerDetalle).not.toHaveBeenCalled();
    expect(screen.getByRole("checkbox", { name: /RAD-0002/ })).toBeChecked();

    await user.click(screen.getByText("RAD-0001"));
    expect(onVerDetalle).toHaveBeenCalledTimes(1);
  });

  it("contrato — sin `seleccionable` no hay columna ni casillas, aunque lleguen las demás props", () => {
    render(
      <ClientProceduresTable
        {...base}
        seleccionable={false}
        seleccion={{ estaSeleccionado: () => true }}
        onToggle={vi.fn(() => true)}
      />,
    );
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: /Selección/ })).not.toBeInTheDocument();
  });
});
