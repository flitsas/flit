// HU #12062 — el organismo decide desde el propio detalle. El pie del modal NO ejecuta nada por su
// cuenta: dispara los mismos manejadores que ya usaba la columna de acciones, así que abre los
// mismos diálogos y aplica las mismas reglas. Se prueba desde la sección —y no desde el modal
// suelto— porque lo que hay que demostrar es justamente que ambas vías desembocan en lo mismo.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProceduresSection } from "../ClientProceduresSection";
import type { OtClientProcedure } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedures: vi.fn(),
  searchOtClientProcedures: vi.fn(),
  fetchOtBandejaFilterFields: vi.fn(),
  fetchOtBandejaHealth: vi.fn(),
  fetchOtBandejaCounters: vi.fn(),
  fetchOtProfile: vi.fn(),
  approveOtClientProcedure: vi.fn(),
  rejectOtClientProcedure: vi.fn(),
  generarOtConsolidadoMaestro: vi.fn(),
  fetchOtClientProcedure: vi.fn(),
  fetchOtDocuments: vi.fn(),
  fetchOtAttachmentPreviewUrl: vi.fn(),
  adjuntarOtLicenciaTransito: vi.fn(),
}));

vi.mock("@/lib/api/ot-metrics", () => ({ fetchRejectionReasons: vi.fn().mockResolvedValue([]) }));

vi.mock("@/lib/api/admin-plate-ranges", () => ({
  listPlateDetails: vi.fn().mockResolvedValue([]),
  assignPlateToProcedure: vi.fn(),
  releaseProcedurePlate: vi.fn(),
}));

vi.mock("@/lib/api/admin-mandate-signers", () => ({ fetchMandateSigners: vi.fn() }));
vi.mock("@/lib/api/download", () => ({ downloadFile: vi.fn() }));

let mockSuperAdmin = false;
vi.mock("@/lib/auth/jwt", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/auth/jwt")>();
  return { ...actual, isSuperAdmin: () => mockSuperAdmin };
});

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    analyzeDocument: vi.fn(),
    listPublishedProcedureTypes: vi.fn().mockResolvedValue([]),
  },
}));

import { assignPlateToProcedure, releaseProcedurePlate } from "@/lib/api/admin-plate-ranges";
import {
  fetchOtBandejaCounters,
  fetchOtBandejaHealth,
  fetchOtClientProcedure,
  searchOtClientProcedures,
  fetchOtBandejaFilterFields,
  fetchOtDocuments,
  fetchOtProfile,
} from "@/lib/api/admin-ot";

const ENTREGADO: OtClientProcedure = {
  id: "proc-1",
  clientTenantId: "client-tenant-aaaa",
  clientTenantName: "Flota Andina S.A.S.",
  procedureTypeId: "matricula-type-id",
  procedureTypeName: "Matrícula inicial",
  referenceNumber: "RAD-2026-101",
  status: "entregado",
  familia: "MATRICULAS",
  createdAt: "2026-08-01T09:00:00Z",
  placa: "ABC123",
};

function prepararBandeja(row: OtClientProcedure) {
  vi.mocked(fetchOtBandejaFilterFields).mockResolvedValue([]);
  vi.mocked(searchOtClientProcedures).mockResolvedValue({
    data: [row],
    totalCount: 1,
    page: 1,
    pageSize: 20,
  });
  vi.mocked(fetchOtClientProcedure).mockResolvedValue(row);
}

const OT_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

/**
 * `transitOfficeId` NO es decorativo en las pruebas de recarga: sin él, `scope` vale `undefined`
 * en todos los renders y la identidad del objeto —que es justo lo que se está probando— nunca
 * cambia. La ruta real del organismo SÍ lo lleva.
 */
function renderSection(transitOfficeId?: string) {
  return render(
    <ToastProvider>
      <ClientProceduresSection transitOfficeId={transitOfficeId} />
    </ToastProvider>,
  );
}

/** Abre el detalle del trámite desde el menú de acciones de su fila. */
async function abrirDetalle(user: ReturnType<typeof userEvent.setup>, radicado: string) {
  await screen.findByText(radicado);
  await user.click(screen.getByRole("button", { name: `Acciones del trámite ${radicado}` }));
  await user.click(await screen.findByRole("menuitem", { name: "Detalle del trámite" }));
  return screen.findByRole("dialog");
}

describe("Detalle OT — decidir desde el modal (HU #12062)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockSuperAdmin = false;
    prepararBandeja(ENTREGADO);
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "dashboard",
      quipuxReadOnly: false,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      featureFlags: [],
      revocationWindowBusinessDays: null,
    });
    vi.mocked(fetchOtBandejaHealth).mockResolvedValue({
      transitOfficeResolved: true,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      deliveredTotal: 1,
      deliveredWithGrant: 1,
      deliveredWithoutGrant: 0,
      hasDeliveredWithoutGrant: false,
    });
    vi.mocked(fetchOtBandejaCounters).mockResolvedValue({
      transitOfficeResolved: true,
      preasignacion: 0,
      asignados: 0,
      porDecidir: 1,
      aprobados: 0,
      rechazados: 0,
      revocados: 0,
      solicitudesRevocatoria: 0,
    });
    vi.mocked(fetchOtDocuments).mockResolvedValue({
      data: [
        {
          id: "d1",
          tipo: "fur",
          filename: "fur.pdf",
          mimetype: "application/pdf",
          sizeBytes: 100,
          sha256: "a",
          source: "upload",
          uploadedAt: "2026-08-02T10:00:00Z",
        },
      ],
      consolidado: false,
      consolidado_maestro: false,
    });
  });

  it("AC1 — el pie del detalle ofrece rechazar y aprobar", async () => {
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    expect(within(dialog).getByRole("button", { name: "Rechazar trámite" })).toBeEnabled();
    expect(within(dialog).getByRole("button", { name: "Aprobar trámite" })).toBeEnabled();
  });

  it("AC2 — rechazar abre el diálogo de motivos ya existente, por encima del detalle", async () => {
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    await user.click(within(dialog).getByRole("button", { name: "Rechazar trámite" }));

    const motivos = await screen.findByRole("dialog", { name: "Rechazar trámite" });
    // Se apila por encima del detalle (z-[1100]); si no, el clic parecería no hacer nada.
    expect(motivos.className).toContain("z-[1200]");
    // Y conserva su validación: sin observación no se puede confirmar.
    expect(within(motivos).getByRole("button", { name: "Confirmar rechazo" })).toBeDisabled();
  });

  it("AC3 — aprobar abre el diálogo de licencia de tránsito ya existente", async () => {
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    await user.click(within(dialog).getByRole("button", { name: "Aprobar trámite" }));

    const aprobacion = await screen.findByRole("dialog", { name: "Confirmar aprobación" });
    expect(aprobacion.className).toContain("z-[1200]");
    expect(within(aprobacion).getByLabelText(/Licencia de Tránsito/i)).toBeInTheDocument();
  });

  it("AC4 — las acciones de la fila siguen intactas en la bandeja", async () => {
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("RAD-2026-101");
    await user.click(
      screen.getByRole("button", { name: "Acciones del trámite RAD-2026-101" }),
    );

    expect(await screen.findByRole("menuitem", { name: "Aprobar" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Rechazar" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Detalle del trámite" })).toBeInTheDocument();
  });

  it("AC5 — un trámite en asignado (ADR-0059) avisa que espera al gestor y deshabilita la decisión", async () => {
    // En la cola de placa el organismo no decide: en `asignado` la pelota está en el gestor
    // (SOAT, impuestos, enviar al OT). El estado ya no es 'entregado', así que el aviso es el del
    // estado y el pendiente lo explica.
    const enRuta = { ...ENTREGADO, status: "asignado" };
    prepararBandeja(enRuta);
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    expect(within(dialog).getByText(/gestione SOAT e impuestos/i)).toBeInTheDocument();

    const aprobar = within(dialog).getByRole("button", { name: "Aprobar trámite" });
    expect(aprobar).toBeDisabled();
    expect(within(dialog).getByRole("button", { name: "Rechazar trámite" })).toBeDisabled();
    // El botón deshabilitado apunta a la franja que explica el porqué.
    const describedBy = aprobar.getAttribute("aria-describedby");
    expect(describedBy).toBeTruthy();
    expect(document.getElementById(describedBy!)).toHaveTextContent(
      /solo decide sobre los que tiene entregados/i,
    );
  });

  it("ADR-0059 — en preasignacion el detalle permite rechazar pero no aprobar, igual que la fila", async () => {
    prepararBandeja({ ...ENTREGADO, status: "preasignacion", placa: null });
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    expect(within(dialog).getByText(/Pendiente asignar placa por el OT/i)).toBeInTheDocument();
    expect(within(dialog).getByText(/solo se aprueba una vez entregado/i)).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Aprobar trámite" })).toBeDisabled();
    expect(within(dialog).getByRole("button", { name: "Rechazar trámite" })).toBeEnabled();
  });

  it("AC5 — un trámite ya resuelto dice que no admite decisión", async () => {
    prepararBandeja({ ...ENTREGADO, status: "aprobado" });
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    expect(
      within(dialog).getByText(/el organismo solo decide sobre los que tiene entregados/i),
    ).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Aprobar trámite" })).toBeDisabled();
  });

  it("AC6 — el SuperAdmin supervisa sin pie de decisión", async () => {
    mockSuperAdmin = true;
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    expect(within(dialog).queryByRole("button", { name: "Aprobar trámite" })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: "Rechazar trámite" })).not.toBeInTheDocument();
  });

  it("AC6 — en modo Quipux de solo lectura tampoco hay pie", async () => {
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "quipux",
      quipuxReadOnly: true,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      featureFlags: [],
      revocationWindowBusinessDays: null,
    });
    const user = userEvent.setup();
    renderSection();

    await waitFor(() => expect(fetchOtProfile).toHaveBeenCalled());
    const dialog = await abrirDetalle(user, "RAD-2026-101");
    expect(within(dialog).queryByRole("button", { name: "Aprobar trámite" })).not.toBeInTheDocument();
  });

  it("escribir el motivo de rechazo NO recarga el detalle de detrás", async () => {
    // El detalle se quedaba abierto detrás del diálogo y saltaba con cada tecla: `scope` era un
    // objeto literal creado en cada render, así que cambiaba de identidad con cada pulsación y
    // relanzaba el efecto que trae el trámite, replegando los acordeones a media escritura.
    const user = userEvent.setup();
    renderSection(OT_ID);

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    await waitFor(() => expect(fetchOtClientProcedure).toHaveBeenCalledTimes(1));
    const acordeon = within(dialog).getByRole("button", {
      name: "Detalles del trámite y vehículo",
    });
    expect(acordeon).toHaveAttribute("aria-expanded", "true");

    await user.click(within(dialog).getByRole("button", { name: "Rechazar trámite" }));
    const motivos = await screen.findByRole("dialog", { name: "Rechazar trámite" });
    await user.type(within(motivos).getByRole("textbox"), "Faltan improntas legibles");

    // Ni una recarga más, y el acordeón sigue como estaba.
    expect(fetchOtClientProcedure).toHaveBeenCalledTimes(1);
    expect(fetchOtDocuments).toHaveBeenCalledTimes(1);
    expect(acordeon).toHaveAttribute("aria-expanded", "true");
  });

  it("escribir la placa a asignar tampoco recarga el detalle", async () => {
    prepararBandeja({ ...ENTREGADO, status: "preasignacion", placa: null });
    const user = userEvent.setup();
    renderSection(OT_ID);

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    await waitFor(() => expect(fetchOtClientProcedure).toHaveBeenCalledTimes(1));

    await user.click(within(dialog).getByRole("button", { name: "Asignar placa" }));
    const placas = await screen.findByRole("dialog", { name: "Asignar placa" });
    await user.type(within(placas).getAllByRole("textbox")[0]!, "ABC123");

    expect(fetchOtClientProcedure).toHaveBeenCalledTimes(1);
  });

  it("asignar la placa desde el detalle la refleja al instante, sin cerrar ni recargar", async () => {
    // Defecto reportado tras la validación en DEV: el detalle es un objeto de estado APARTE del de
    // la fila, así que seguía enseñando «Sin preasignar» hasta cerrar el modal y recargar la
    // bandeja. El operador se quedaba esperando algo que ya había ocurrido.
    const sinPlaca = { ...ENTREGADO, status: "preasignacion", placa: null };
    prepararBandeja(sinPlaca);
    // El backend devuelve el trámite ya con placa en la siguiente lectura, como en producción.
    vi.mocked(assignPlateToProcedure).mockImplementation(async () => {
      vi.mocked(fetchOtClientProcedure).mockResolvedValue({
        ...sinPlaca,
        placa: "XYZ987",
        status: "asignado",
      });
    });
    const user = userEvent.setup();
    renderSection(OT_ID);

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    await waitFor(() => expect(fetchOtClientProcedure).toHaveBeenCalledTimes(1));
    expect(within(dialog).getByText("Sin preasignar")).toBeInTheDocument();

    await user.click(within(dialog).getByRole("button", { name: "Asignar placa" }));
    const placas = await screen.findByRole("dialog", { name: "Asignar placa" });
    await user.type(within(placas).getByLabelText("Placa fuera de rango"), "XYZ987");
    await user.click(within(placas).getByRole("button", { name: "Asignar" }));

    // El diálogo se cierra y el detalle —que sigue abierto— ya muestra la placa.
    await waitFor(() =>
      expect(screen.queryByRole("dialog", { name: "Asignar placa" })).not.toBeInTheDocument(),
    );
    const detalle = screen.getByRole("dialog");
    expect(within(detalle).getByText("XYZ987")).toBeInTheDocument();
    expect(within(detalle).queryByText("Sin preasignar")).not.toBeInTheDocument();
  });

  it("el acordeón que el operador tenía abierto sobrevive a la asignación de placa", async () => {
    prepararBandeja({ ...ENTREGADO, status: "preasignacion", placa: null });
    vi.mocked(assignPlateToProcedure).mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderSection(OT_ID);

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    await waitFor(() => expect(fetchOtClientProcedure).toHaveBeenCalledTimes(1));
    const actores = within(dialog).getByRole("button", { name: "Actores del Trámite" });
    await user.click(actores);
    expect(actores).toHaveAttribute("aria-expanded", "true");

    await user.click(within(dialog).getByRole("button", { name: "Asignar placa" }));
    const placas = await screen.findByRole("dialog", { name: "Asignar placa" });
    await user.type(within(placas).getByLabelText("Placa fuera de rango"), "XYZ987");
    await user.click(within(placas).getByRole("button", { name: "Asignar" }));

    await waitFor(() =>
      expect(screen.queryByRole("dialog", { name: "Asignar placa" })).not.toBeInTheDocument(),
    );
    expect(actores).toHaveAttribute("aria-expanded", "true");
  });

  it("HU #12602 AC6 — liberar la placa devuelve el trámite a Preasignación y lo saca de «Asignados»", async () => {
    // Liberar suelta la reserva del inventario y devuelve el trámite a la cola de placa; el backend
    // deja el field_value 'plate' escrito (HU #12077) y la fila NO la borra de forma optimista
    // (`liberado` solo cambia el estado). Con la bandeja filtrada por estado, el trámite ya no
    // pertenece a la tarjeta activa y se retira en vez de quedarse con un chip que no cuadra.
    prepararBandeja({ ...ENTREGADO, status: "asignado", placa: "OTV120" });
    vi.mocked(releaseProcedurePlate).mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderSection(OT_ID);

    await screen.findByText("OTV120");
    await user.click(
      screen.getByRole("button", { name: "Acciones del trámite RAD-2026-101" }),
    );
    // AC4 — en asignado el menú ofrece Actualizar placa y Liberar placa; ni Aprobar, ni Rechazar, ni Adjuntar LT.
    expect(await screen.findByRole("menuitem", { name: "Liberar placa" })).toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "Aprobar" })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "Rechazar" })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "Adjuntar LT" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("menuitem", { name: "Liberar placa" }));

    const liberar = await screen.findByRole("dialog", { name: "Liberar placa" });
    await user.type(within(liberar).getByRole("textbox"), "Placa mal digitada");
    await user.click(within(liberar).getByRole("button", { name: "Liberar placa" }));

    expect(releaseProcedurePlate).toHaveBeenCalledWith("proc-1", "Placa mal digitada");
    // Preasignación no es la tarjeta activa: la fila se va sin recargar y sin fingir que la placa
    // se borró (cuando reaparezca bajo «Preasignación» traerá OTV120, como la deja el servidor).
    await waitFor(() => expect(screen.queryByText("RAD-2026-101")).not.toBeInTheDocument());
  });

  it("HU #12602 AC3 — en preasignacion el menú ofrece Asignar placa y Rechazar; ni Aprobar ni Adjuntar LT", async () => {
    prepararBandeja({ ...ENTREGADO, status: "preasignacion", placa: null });
    const user = userEvent.setup();
    renderSection(OT_ID);

    await screen.findByText("RAD-2026-101");
    await user.click(
      screen.getByRole("button", { name: "Acciones del trámite RAD-2026-101" }),
    );
    expect(await screen.findByRole("menuitem", { name: "Asignar placa" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Rechazar" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Detalle del trámite" })).toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "Aprobar" })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "Adjuntar LT" })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "Liberar placa" })).not.toBeInTheDocument();
  });

  it("la bandeja se actualiza con un botón, sin recargar la página", async () => {
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("RAD-2026-101");
    const llamadasIniciales = vi.mocked(searchOtClientProcedures).mock.calls.length;

    await user.click(screen.getByRole("button", { name: "Actualizar la bandeja de trámites" }));
    await waitFor(() =>
      expect(vi.mocked(searchOtClientProcedures).mock.calls.length).toBeGreaterThan(
        llamadasIniciales,
      ),
    );
  });

  it("la placa sin preasignar se resuelve desde su propia celda del vehículo", async () => {
    // El prototipo pone la preasignación EN la celda de la placa, no en el pie: es la celda que
    // reporta el problema, así que es donde se espera el remedio.
    prepararBandeja({
      ...ENTREGADO,
      status: "preasignacion",
      placa: null,
      platePreferredLastDigit: "3",
    });
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    expect(within(dialog).getByText("Sin preasignar")).toBeInTheDocument();
    expect(within(dialog).getByText("Dígito preferido: 3")).toBeInTheDocument();
    // Sigue sin poder decidirse, pero desde aquí se desbloquea.
    expect(within(dialog).getByRole("button", { name: "Aprobar trámite" })).toBeDisabled();

    await user.click(within(dialog).getByRole("button", { name: "Asignar placa" }));
    expect(await screen.findByRole("dialog", { name: "Asignar placa" })).toBeInTheDocument();
  });

  it("la fila entera abre el detalle, y el menú de acciones no lo dispara", async () => {
    const user = userEvent.setup();
    renderSection();

    // Pulsar cualquier celda de la fila lleva al detalle: es a donde se entra casi siempre.
    await user.click(await screen.findByText("RAD-2026-101"));
    expect(await screen.findByRole("dialog")).toHaveTextContent("Gestión y Aprobación del Trámite");
  });

  it("pulsar el menú de acciones NO abre además el detalle", async () => {
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("RAD-2026-101");
    await user.click(screen.getByRole("button", { name: "Acciones del trámite RAD-2026-101" }));

    // El menú se abre y el detalle NO: si la fila se tragara el clic, elegir «Aprobar» dejaría el
    // detalle abierto por debajo del diálogo.
    expect(await screen.findByRole("menuitem", { name: "Aprobar" })).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
