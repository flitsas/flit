// Tests del detalle del trámite abierto desde el historial por placa (Feature #12189 · HU #12195).
//
// Cubre los criterios de la HU:
//   AC1 — cada fila abre el MISMO modal de detalle del módulo de Trámites, con el trámite de esa fila.
//   AC2 — desde el historial el detalle es de solo lectura: ninguna acción de escritura ofrecida.
//   AC3 — el modal se cierra con `Esc`.
//   AC4 — al cerrarse, el foco vuelve al botón que lo abrió (WCAG 2.1 AA, 2.4.3).
//
// Se mockea `tramitesClient` completo (el modal consulta detalle, adjuntos y expediente biométrico)
// y se renderizan los componentes reales, sin red.
//
// Uso de ejemplo:
//   render(<HistorialPlaca />) → consultar "ABC123" → clic en "Ver detalle del trámite RAD-0001"
//   abre `TramiteDetalleModal` en modo `readOnly`.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type { InstanceSummary } from "@/lib/api/types/procedure-runtime";

const mocks = vi.hoisted(() => ({
  listPlateHistory: vi.fn(),
  getInstance: vi.fn(),
  getAttachments: vi.fn(),
  listBiometricExpediente: vi.fn(),
  startSubsanacion: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
}));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    listPlateHistory: mocks.listPlateHistory,
    getInstance: mocks.getInstance,
    getAttachments: mocks.getAttachments,
    listBiometricExpediente: mocks.listBiometricExpediente,
    startSubsanacion: mocks.startSubsanacion,
    fetchAttachmentPreviewUrl: mocks.fetchAttachmentPreviewUrl,
  },
}));

import { HistorialPlaca } from "@/components/atom/modules/HistorialPlaca";
import { TramiteDetalleModal } from "@/components/operacion/TramiteDetalleModal";

function fila(overrides: Partial<InstanceSummary> = {}): InstanceSummary {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    referenceNumber: "RAD-0001",
    modalidad: "TRASPASO",
    tipoNombre: "Traspaso",
    estado: "aprobado",
    placa: "ABC123",
    vin: "9BWZZZ377VT004251",
    vehiculoMarca: "Mazda",
    vehiculoLinea: "CX-5",
    compradorNombre: "Compradora Uno",
    compradorDocumento: "1000000001",
    vendedorNombre: "Vendedor Uno",
    vendedorDocumento: "1000000002",
    organismoTransito: "OT Medellín",
    pasoActual: 5,
    totalPasos: 5,
    createdAt: "2026-08-01T10:00:00Z",
    draftFinalizedAt: null,
    identityValidationStatus: null,
    signaturePending: false,
    canSubmit: true,
    prioritario: false,
    tenantId: "tenant-aaa",
    companiaNombre: "Compañía Alfa",
    updatedAt: "2026-08-05T09:00:00Z",
    gestorNombre: "Gestora Uno",
    subsanacionActiva: false,
    plateFlowStatus: null,
    ultimoRechazoMotivo: null,
    isPaused: false,
    pausedObservation: null,
    ...overrides,
  } as InstanceSummary;
}

const FILA_A = fila();
const FILA_B = fila({
  id: "22222222-2222-2222-2222-222222222222",
  referenceNumber: "RAD-0002",
  estado: "entregado",
  tipoNombre: "Traspaso",
});

beforeEach(() => {
  Object.values(mocks).forEach((m) => m.mockReset());
  mocks.listPlateHistory.mockResolvedValue({ items: [FILA_A, FILA_B], total: 2 });
  mocks.getInstance.mockResolvedValue({
    statusHistory: [
      { fromStatus: null, toStatus: "borrador", changedAt: "2026-08-01T10:00:00Z", reason: null },
    ],
    fieldValues: [],
    actors: [],
  } as never);
  mocks.getAttachments.mockResolvedValue([]);
  mocks.listBiometricExpediente.mockResolvedValue({
    validations: [],
    firmaBaulPartes: [],
    firmaBaulActores: [],
  });
});

async function consultarYAbrir(placa = "ABC123", radicado = "RAD-0001") {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText("Placa"), placa);
  await user.click(screen.getByRole("button", { name: /consultar/i }));
  const disparador = await screen.findByRole("button", {
    name: `Ver detalle del trámite ${radicado}`,
  });
  await user.click(disparador);
  const dialog = await screen.findByRole("dialog");
  return { user, disparador, dialog };
}

describe("HistorialPlaca — detalle del trámite (HU #12195)", () => {
  it("AC1 — cada fila ofrece la acción de ver el detalle, con nombre accesible distinguible", async () => {
    render(<HistorialPlaca />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Placa"), "ABC123");
    await user.click(screen.getByRole("button", { name: /consultar/i }));

    expect(
      await screen.findByRole("button", { name: "Ver detalle del trámite RAD-0001" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Ver detalle del trámite RAD-0002" }),
    ).toBeInTheDocument();
    // Cerrado por defecto: la tabla no monta un diálogo hasta que se pide.
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("AC1 — abre el modal con el trámite de ESA fila (no con el primero de la lista)", async () => {
    render(<HistorialPlaca />);
    const { dialog } = await consultarYAbrir("ABC123", "RAD-0002");

    expect(dialog).toHaveTextContent("RAD-0002");
    expect(dialog).not.toHaveTextContent("RAD-0001");
    await waitFor(() => expect(mocks.getInstance).toHaveBeenCalled());
    expect(mocks.getInstance).toHaveBeenCalledWith(FILA_B.id, undefined);
  });

  it("AC1 — sin SuperAdmin no se manda tenantId: el alcance lo resolvió el servidor", async () => {
    render(<HistorialPlaca />);
    await consultarYAbrir();
    await waitFor(() => expect(mocks.getInstance).toHaveBeenCalledWith(FILA_A.id, undefined));
  });

  it("AC1 — SuperAdmin sí manda el tenantId de la fila (consulta cross-compañía)", async () => {
    render(<HistorialPlaca isSuperAdmin />);
    await consultarYAbrir();
    await waitFor(() =>
      expect(mocks.getInstance).toHaveBeenCalledWith(FILA_A.id, "tenant-aaa"),
    );
  });

  it("AC2 — el detalle abierto desde el historial no ofrece ninguna acción de escritura", async () => {
    // Peor escenario para la regla: trámite RECHAZADO con subsanación activa, que en el módulo de
    // Trámites es justo el caso donde aparece la CTA de subsanar (único POST del modal).
    mocks.listPlateHistory.mockResolvedValue({
      items: [
        fila({
          estado: "rechazado",
          subsanacionActiva: true,
          ultimoRechazoMotivo: "Faltó el SOAT vigente",
        }),
      ],
      total: 1,
    });
    render(<HistorialPlaca />);
    const { dialog } = await consultarYAbrir();

    expect(within(dialog).queryByRole("button", { name: /subsanar/i })).not.toBeInTheDocument();
    expect(
      within(dialog).queryByRole("button", { name: /continuar la subsanación/i }),
    ).not.toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: /editar|guardar|aprobar|rechazar|radicar|anular/i }))
      .not.toBeInTheDocument();
    expect(mocks.startSubsanacion).not.toHaveBeenCalled();
    // El motivo del rechazo sí se muestra: es información, no acción.
    expect(dialog).toHaveTextContent("Faltó el SOAT vigente");
  });

  it("AC3 — Esc cierra el modal", async () => {
    render(<HistorialPlaca />);
    const { user } = await consultarYAbrir();

    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("AC4 — al cerrar, el foco vuelve al botón que abrió el modal", async () => {
    render(<HistorialPlaca />);
    const { user, disparador, dialog } = await consultarYAbrir();

    // Mientras está abierto el foco vive dentro del diálogo, no en el disparador.
    await waitFor(() => expect(dialog.contains(document.activeElement)).toBe(true));

    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await waitFor(() => expect(document.activeElement).toBe(disparador));
  });

  it("AC3 — el botón Cerrar del modal también lo cierra y devuelve el foco", async () => {
    render(<HistorialPlaca />);
    const { user, disparador, dialog } = await consultarYAbrir();

    await user.click(within(dialog).getByRole("button", { name: "Cerrar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await waitFor(() => expect(document.activeElement).toBe(disparador));
  });
});

describe("TramiteDetalleModal — contrato de solo lectura (HU #12195)", () => {
  const RECHAZADO = fila({ estado: "rechazado", ultimoRechazoMotivo: "Faltó el SOAT vigente" });

  it("contrato — con readOnly apaga la CTA de subsanar aunque reciba onAbrirAsistente", async () => {
    render(
      <TramiteDetalleModal
        open
        instanceId={RECHAZADO.id}
        item={RECHAZADO}
        onClose={() => undefined}
        onAbrirAsistente={() => undefined}
        readOnly
      />,
    );
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).queryByRole("button", { name: /subsanar/i })).not.toBeInTheDocument();
  });

  it("contrato — sin readOnly el módulo de Trámites conserva la CTA (no se borró la función)", async () => {
    render(
      <TramiteDetalleModal
        open
        instanceId={RECHAZADO.id}
        item={RECHAZADO}
        onClose={() => undefined}
        onAbrirAsistente={() => undefined}
      />,
    );
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("button", { name: /subsanar trámite/i })).toBeInTheDocument();
  });

  it("edge case — el foco queda dentro del diálogo al abrir (Tab no escapa al fondo)", async () => {
    render(
      <TramiteDetalleModal
        open
        instanceId={FILA_A.id}
        item={FILA_A}
        onClose={() => undefined}
        readOnly
      />,
    );
    const dialog = await screen.findByRole("dialog");
    await waitFor(() => expect(dialog.contains(document.activeElement)).toBe(true));

    const user = userEvent.setup();
    await user.tab();
    expect(dialog.contains(document.activeElement)).toBe(true);
  });
});
