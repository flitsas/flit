// HU #12311 (Feature #12276) — pestaña Historial: tabla, filtros en la URL, detalle con crudo,
// «Consultar ahora» con confirmación inline y estados vacío/error.
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "@/components/admin/Toast";
import { ConfirmacionRuntHistorialPanel } from "../ConfirmacionRuntHistorialPanel";

const replace = vi.fn();
let search = "";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace, push: vi.fn() }),
  usePathname: () => "/admin/plataforma/confirmacion-runt/historial",
  useSearchParams: () => new URLSearchParams(search),
}));

const listRuntConfirmationAttempts = vi.fn();
const listRuntConfirmationRuns = vi.fn();
const getRuntConfirmationAttempt = vi.fn();
const getRuntConfirmationAttemptRaw = vi.fn();
const consultRuntNow = vi.fn();

vi.mock("@/lib/api/admin-runt-confirmation", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-runt-confirmation")>(
    "@/lib/api/admin-runt-confirmation",
  );
  return {
    ...actual,
    listRuntConfirmationAttempts: (...a: unknown[]) => listRuntConfirmationAttempts(...a),
    listRuntConfirmationRuns: (...a: unknown[]) => listRuntConfirmationRuns(...a),
    getRuntConfirmationAttempt: (...a: unknown[]) => getRuntConfirmationAttempt(...a),
    getRuntConfirmationAttemptRaw: (...a: unknown[]) => getRuntConfirmationAttemptRaw(...a),
    consultRuntNow: (...a: unknown[]) => consultRuntNow(...a),
  };
});

vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: {
    listProcedureTypes: () => Promise.resolve([{ code: "CAMBIO_COLOR", name: "Cambio de color" }]),
  },
}));

const run = {
  id: "run-1",
  trigger: "scheduled",
  startedAt: "2026-09-10T07:00:00Z",
  finishedAt: "2026-09-10T07:00:40Z",
  providerKey: "kyverum_runt",
  skippedReason: null,
  consulted: 5,
  confirmed: 3,
  pending: 1,
  discrepancies: 1,
  unverifiable: 0,
  errors: 0,
  providerCalls: 6,
  errorMessage: null,
};

const fila = {
  id: "a-1",
  queriedAt: "2026-09-10T15:32:18Z",
  procedureInstanceId: "inst-1",
  referenceNumber: "7",
  procedureTypeCode: "MATRICULA_NUEVA",
  procedureTypeName: "Matrícula inicial",
  family: "MATRICULAS",
  plate: "QYS575",
  vin: "LRWYGCFJ8TC768034",
  tenantId: "t-1",
  tenantName: "FLIT SAS",
  providerKey: "kyverum_runt",
  queryKind: "vin",
  attemptNo: 1,
  verdict: "confirmed",
  reasonText: "Solicitud 298997109 MATRICULA INICIAL AUTORIZADA el 2026-07-25 en STRIA DE TTOyTTE MEDELLIN.",
  ruleVersion: "confirmacion-v1",
  runId: "run-1",
  requestedBy: null,
  flagApplied: null,
  hasRaw: true,
};

const pendiente = { ...fila, id: "a-2", verdict: "pending", reasonText: "No hay solicitud de TRASPASO posterior a la radicación.", attemptNo: 2 };

function renderPanel() {
  return render(
    <ToastProvider>
      <ConfirmacionRuntHistorialPanel />
    </ToastProvider>,
  );
}

describe("ConfirmacionRuntHistorialPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    search = "";
    listRuntConfirmationAttempts.mockResolvedValue({ items: [fila, pendiente], total: 2, page: 1, pageSize: 25 });
    listRuntConfirmationRuns.mockResolvedValue({ items: [run], total: 1, page: 1, pageSize: 30 });
    getRuntConfirmationAttempt.mockImplementation(async (id: string) => ({
      row: id === "a-1" ? fila : pendiente,
      rawPayloadId: "p-1",
      sellerRawPayloadId: null,
      reevaluatedFromAttemptId: null,
      procedureStatus: "aprobado",
      runtConfirmedAt: id === "a-1" ? "2026-09-10T15:32:22Z" : null,
      runtAttempts: 1,
      runtFlag: null,
    }));
    getRuntConfirmationAttemptRaw.mockResolvedValue({
      primary: {
        ok: true,
        data: {
          vehiculo: { placa: "QYS575", estadoAutomotor: "ACTIVO", mostrarSolicitudes: "SI" },
          solicitudes: [{ noSolicitud: "298997109", fechaSolicitud: "2026-07-25T10:00:00.000-05:00", estado: "AUTORIZADA", tramitesRealizados: "TRÁMITE MATRÍCULA INICIAL, ", entidad: "STRIA DE TTOyTTE MEDELLIN" }],
        },
      },
    });
  });

  it("AC1 — lista los intentos con trámite, tipo, placa, proveedor, número, píldora con texto y botón Ver", async () => {
    renderPanel();

    const tabla = await screen.findByRole("table", { name: "Historial de intentos de confirmación RUNT" });
    expect(within(tabla).getAllByText("7")).toHaveLength(2);
    expect(within(tabla).getAllByText("Matrícula inicial")).toHaveLength(2);
    expect(within(tabla).getAllByText("QYS575")).toHaveLength(2);
    expect(within(tabla).getAllByText("Kyverum")).toHaveLength(2);
    expect(within(tabla).getByText("Confirmado")).toBeInTheDocument();
    expect(within(tabla).getByText("Pendiente")).toBeInTheDocument();
    expect(within(tabla).getAllByRole("button", { name: "Ver" })).toHaveLength(2);
    expect(listRuntConfirmationAttempts).toHaveBeenCalledWith(expect.objectContaining({}), 1, 25, expect.anything());
  });

  it("AC2 — cambiar un filtro lo lleva a la URL; filtrar por corrida muestra su resumen", async () => {
    search = "runId=run-1";
    renderPanel();
    const user = userEvent.setup();

    const resumen = await screen.findByTestId("confirmacion-runt-ultima-corrida");
    expect(within(resumen).getByText("Resumen de la corrida")).toBeInTheDocument();
    expect(within(resumen).getByText(/6 llamadas al proveedor/)).toBeInTheDocument();
    expect(listRuntConfirmationAttempts).toHaveBeenCalledWith(expect.objectContaining({ runId: "run-1" }), 1, 25, expect.anything());

    await user.selectOptions(screen.getByLabelText("Resultado"), "pending");
    expect(replace).toHaveBeenCalledWith("/admin/plataforma/confirmacion-runt/historial?runId=run-1&verdict=pending");

    await user.type(screen.getByLabelText("Trámite o placa"), "QYS575");
    await user.click(screen.getByRole("button", { name: "Buscar" }));
    expect(replace).toHaveBeenLastCalledWith("/admin/plataforma/confirmacion-runt/historial?runId=run-1&q=QYS575");
  });

  it("AC3 — Ver expande el detalle: veredicto con motivo y proveedor; la respuesta en secciones y el JSON en pestañas", async () => {
    renderPanel();
    const user = userEvent.setup();

    await user.click((await screen.findAllByRole("button", { name: "Ver" }))[0]);
    const detalle = await screen.findByTestId("intento-detalle");
    expect(within(detalle).getByTestId("intento-motivo")).toHaveTextContent(/Solicitud 298997109/);
    expect(within(detalle).getByText("Kyverum")).toBeInTheDocument();
    expect(within(detalle).getByText("Corrida programada")).toBeInTheDocument();
    expect(within(detalle).getByText(/Confirmado en el RUNT: sale de las corridas/)).toBeInTheDocument();
    // La versión de la regla ya no se muestra: no le dice nada al usuario (sigue en el export).
    expect(within(detalle).queryByText("confirmacion-v1")).not.toBeInTheDocument();

    // Respuesta del RUNT en secciones de negocio, desde el mismo crudo (se pide una sola vez).
    await user.click(within(detalle).getByRole("tab", { name: "Respuesta del RUNT" }));
    expect(await within(detalle).findByText("Vehículo en el RUNT")).toBeInTheDocument();
    expect(within(detalle).getByText(/Solicitudes ante el RUNT/)).toBeInTheDocument();
    expect(within(detalle).getByText("25/07/2026")).toBeInTheDocument();
    expect(within(detalle).getByText("AUTORIZADA")).toBeInTheDocument();
    expect(getRuntConfirmationAttemptRaw).toHaveBeenCalledWith("a-1", expect.anything());

    await user.click(within(detalle).getByRole("tab", { name: "JSON" }));
    const raw = await screen.findByTestId("intento-raw");
    expect(raw).toHaveTextContent('"solicitudes"');
    expect(getRuntConfirmationAttemptRaw).toHaveBeenCalledTimes(1);
  });

  it("AC4 — Consultar ahora pide confirmación, llama al endpoint y recarga; un 409 muestra el motivo sin recargar", async () => {
    const { ConsultNowConflictError } = await import("@/lib/api/admin-runt-confirmation");
    consultRuntNow.mockResolvedValueOnce({ attempt: { ...pendiente, id: "a-3", verdict: "confirmed" }, run });
    renderPanel();
    const user = userEvent.setup();

    // El intento a-2 es de un trámite aprobado y sin confirmar: sí ofrece Consultar ahora.
    await user.click((await screen.findAllByRole("button", { name: "Ver" }))[1]);
    const boton = await screen.findByTestId("consultar-ahora");
    await user.click(boton);
    expect(consultRuntNow).not.toHaveBeenCalled();
    await user.click(screen.getByTestId("consultar-ahora-confirmar"));

    await waitFor(() => expect(consultRuntNow).toHaveBeenCalledWith("inst-1"));
    await waitFor(() => expect(listRuntConfirmationAttempts).toHaveBeenCalledTimes(2));
    expect(await screen.findByText(/Consulta realizada: Confirmado/)).toBeInTheDocument();

    // 409: el motivo se muestra en el detalle y la tabla no se vuelve a pedir.
    consultRuntNow.mockRejectedValueOnce(new ConsultNowConflictError("ya_confirmado"));
    await user.click((await screen.findAllByRole("button", { name: "Ver" }))[1]);
    await user.click(await screen.findByTestId("consultar-ahora"));
    await user.click(screen.getByTestId("consultar-ahora-confirmar"));
    expect(await screen.findByText("El trámite ya está confirmado en el RUNT.")).toBeInTheDocument();
    expect(listRuntConfirmationAttempts).toHaveBeenCalledTimes(2);
  });

  it("AC3 — un intento ya confirmado no ofrece Consultar ahora", async () => {
    renderPanel();
    const user = userEvent.setup();
    await user.click((await screen.findAllByRole("button", { name: "Ver" }))[0]);
    await screen.findByTestId("intento-detalle");
    await waitFor(() => expect(getRuntConfirmationAttempt).toHaveBeenCalledWith("a-1", expect.anything()));
    expect(screen.queryByTestId("consultar-ahora")).not.toBeInTheDocument();
  });

  it("AC6 — sin intentos muestra el vacío con mensaje claro; si falla, el error con reintento", async () => {
    listRuntConfirmationAttempts.mockResolvedValueOnce({ items: [], total: 0, page: 1, pageSize: 25 });
    renderPanel();
    expect(await screen.findByText(/Todavía no hay intentos de confirmación/)).toBeInTheDocument();
  });

  it("AC6 — error de carga", async () => {
    listRuntConfirmationAttempts.mockRejectedValueOnce(new Error("boom"));
    renderPanel();
    expect(await screen.findByText("No se pudo cargar el historial de confirmación.")).toBeInTheDocument();
  });
});
