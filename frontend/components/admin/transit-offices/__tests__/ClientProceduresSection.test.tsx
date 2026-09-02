// HU #10220 — Vista tenant admin: aprobar/rechazar trámites de clientes.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProceduresSection } from "../ClientProceduresSection";
import type { OtClientProcedure } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedures: vi.fn(),
  fetchOtBandejaHealth: vi.fn(),
  fetchOtProfile: vi.fn(),
  approveOtClientProcedure: vi.fn(),
  rejectOtClientProcedure: vi.fn(),
  generarOtConsolidadoMaestro: vi.fn(),
  fetchOtDocuments: vi.fn(),
  fetchOtAttachmentPreviewUrl: vi.fn(),
  adjuntarOtLicenciaTransito: vi.fn(),
}));

// N 03 fix — rol simulable: SuperAdmin supervisa la cola pero no decide (approve/reject
// no soportan su override de organismo); ot_admin conserva las acciones.
let mockSuperAdmin = false;
vi.mock("@/lib/auth/jwt", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/auth/jwt")>();
  return { ...actual, isSuperAdmin: () => mockSuperAdmin };
});

vi.mock("@/lib/api/admin-mandate-signers", () => ({
  fetchMandateSigners: vi.fn(),
}));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    // HU #12042 — la LT se analiza al SELECCIONARLA, para que el OT vea el veredicto antes de decidir.
    analyzeDocument: vi.fn(),
    listPublishedProcedureTypes: vi.fn().mockResolvedValue([
      {
        id: "matricula_inicial-type-id",
        code: "matricula_inicial",
        name: "Matrícula inicial",
        family: "MATRICULAS",
        publicationStatus: "published",
        isActive: true,
        publishedAt: null,
      },
    ]),
  },
}));

import { tramitesClient } from "@/lib/api/tramites-client";
import {
  adjuntarOtLicenciaTransito,
  approveOtClientProcedure,
  fetchOtAttachmentPreviewUrl,
  fetchOtBandejaHealth,
  fetchOtClientProcedures,
  fetchOtProfile,
  generarOtConsolidadoMaestro,
  rejectOtClientProcedure,
} from "@/lib/api/admin-ot";
import { fetchMandateSigners } from "@/lib/api/admin-mandate-signers";
import { ApiError } from "@/lib/api/types";

const procedure: OtClientProcedure = {
  id: "proc-1",
  clientTenantId: "client-tenant-aaaa",
  clientTenantName: "Flota Andina S.A.S.",
  procedureTypeId: "matricula_inicial-type-id",
  procedureTypeName: "Matrícula inicial",
  referenceNumber: "RAD-2026-001",
  status: "entregado",
  createdAt: "2026-06-23T09:00:00Z",
};

function renderSection(transitOfficeId?: string) {
  return render(
    <ToastProvider>
      <ClientProceduresSection transitOfficeId={transitOfficeId} />
    </ToastProvider>,
  );
}

describe("ClientProceduresSection — HU #10220", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockSuperAdmin = false;
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "dashboard",
      quipuxReadOnly: false,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      featureFlags: [],
    });
    vi.mocked(fetchOtClientProcedures).mockResolvedValue({
      data: [procedure],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });
    // Por defecto la bandeja está sana (sin entregados huérfanos): el banner no se muestra.
    vi.mocked(fetchOtBandejaHealth).mockResolvedValue({
      transitOfficeResolved: true,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      deliveredTotal: 1,
      deliveredWithGrant: 1,
      deliveredWithoutGrant: 0,
      hasDeliveredWithoutGrant: false,
    });
    vi.mocked(approveOtClientProcedure).mockResolvedValue({
      ...procedure,
      status: "aprobado",
    });
    vi.mocked(rejectOtClientProcedure).mockResolvedValue({
      ...procedure,
      status: "rechazado",
    });
  });

  it("AC1 muestra tabla con columnas requeridas", async () => {
    renderSection();
    expect(await screen.findByText("RAD-2026-001")).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Matrícula inicial" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Flota Andina S.A.S." })).toBeInTheDocument();
    expect(screen.getByRole("status", { name: "Estado: Pendiente OT" })).toBeInTheDocument();
  });

  it("AC2 aprobar con confirmación actualiza fila optimistamente", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByRole("button", { name: /Aprobar/i });
    await user.click(screen.getByRole("button", { name: /Aprobar/i }));
    await user.click(screen.getByRole("button", { name: /Confirmar$/i }));
    await waitFor(() => expect(approveOtClientProcedure).toHaveBeenCalledWith("proc-1", undefined));
    expect(screen.getByRole("status", { name: "Estado: Aprobado OT" })).toBeInTheDocument();
  });

  it("ADR-0036 §D9: 409 mandatario_requerido abre el diálogo y reintenta con el elegido", async () => {
    const user = userEvent.setup();
    const signerBase = {
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      documentType: "CC",
      integrityHash: "h",
      email: null,
      userId: null,
      identityValidationRef: null,
      identityStatus: "none" as const,
      signatureVaultId: null,
      registeredAt: "2026-07-01T00:00:00Z",
      isActive: true,
      companyTenantIds: ["client-tenant-aaaa"],
    };
    vi.mocked(fetchMandateSigners).mockResolvedValue([
      { ...signerBase, id: "signer-1", fullName: "Ana Gómez", documentNumber: "52123456" },
      { ...signerBase, id: "signer-2", fullName: "Luis Ríos", documentNumber: "70111222" },
    ]);
    vi.mocked(approveOtClientProcedure)
      .mockRejectedValueOnce(new ApiError(409, "mandatario_requerido", { error: "mandatario_requerido" }))
      .mockResolvedValueOnce({ ...procedure, status: "aprobado" });

    renderSection();
    await screen.findByRole("button", { name: /Aprobar/i });
    await user.click(screen.getByRole("button", { name: /Aprobar/i }));
    await user.click(screen.getByRole("button", { name: /Confirmar$/i }));

    // Aparece el diálogo de selección de mandatario (varios sin cotejo).
    expect(await screen.findByText(/Elige el mandatario que firma/i)).toBeInTheDocument();
    // Elige el segundo mandatario y reintenta.
    await user.click(screen.getByRole("radio", { name: /Luis Ríos/i }));
    await user.click(screen.getByRole("button", { name: /Aprobar con este mandatario/i }));

    await waitFor(() => expect(approveOtClientProcedure).toHaveBeenLastCalledWith("proc-1", "signer-2"));
    expect(screen.getByRole("status", { name: "Estado: Aprobado OT" })).toBeInTheDocument();
  });

  it("AC3 rechazar deshabilita confirmar sin motivo", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByRole("button", { name: /Rechazar/i });
    await user.click(screen.getByRole("button", { name: /Rechazar/i }));
    const confirm = screen.getByRole("button", { name: /Confirmar rechazo/i });
    expect(confirm).toBeDisabled();
    await user.type(screen.getByRole("textbox"), "Documentación incompleta");
    expect(confirm).not.toBeDisabled();
    await user.click(confirm);
    await waitFor(() =>
      expect(rejectOtClientProcedure).toHaveBeenCalledWith("proc-1", {
        reason: "Documentación incompleta",
      }),
    );
  });

  it("AC4 aplica filtro por estado entregado (pendiente OT, N 03)", async () => {
    const user = userEvent.setup();
    renderSection();
    await waitFor(() =>
      expect(fetchOtClientProcedures).toHaveBeenCalledWith(
        expect.objectContaining({ status: "entregado", pageSize: 20 }),
        expect.anything(),
        undefined,
      ),
    );
    await user.click(screen.getByRole("button", { name: /^Filtros/i }));
    await user.selectOptions(screen.getByLabelText(/Filtrar por tipo de trámite/i), "matricula_inicial-type-id");
    await user.click(screen.getByRole("button", { name: /Aplicar filtros/i }));
    await waitFor(() =>
      expect(fetchOtClientProcedures).toHaveBeenCalledWith(
        expect.objectContaining({
          status: "entregado",
          procedureTypeId: "matricula_inicial-type-id",
        }),
        expect.anything(),
        undefined,
      ),
    );
  });

  it("N03 fix — con transitOfficeId scope-a la lista y el perfil (vista SuperAdmin)", async () => {
    renderSection("aaaaaaaa-0001-4000-8000-000000000001");
    await waitFor(() =>
      expect(fetchOtClientProcedures).toHaveBeenCalledWith(
        expect.objectContaining({ status: "entregado" }),
        expect.anything(),
        { transitOfficeId: "aaaaaaaa-0001-4000-8000-000000000001" },
      ),
    );
    expect(fetchOtProfile).toHaveBeenCalledWith(expect.anything(), {
      transitOfficeId: "aaaaaaaa-0001-4000-8000-000000000001",
    });
    expect(await screen.findByText("RAD-2026-001")).toBeInTheDocument();
  });

  it("N03 fix — SuperAdmin ve la cola pero sin acciones aprobar/rechazar", async () => {
    mockSuperAdmin = true;
    renderSection("aaaaaaaa-0001-4000-8000-000000000001");
    expect(await screen.findByText("RAD-2026-001")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Aprobar$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Rechazar$/i })).not.toBeInTheDocument();
  });

  it("consolidado — botón único asegura el maestro y previsualiza inline (sin descarga)", async () => {
    // Feature #10701: un solo botón "Ver consolidado". El backend es idempotente por la marca de
    // vigencia; el front usa el attachmentId que devuelve la generación y previsualiza inline.
    vi.mocked(generarOtConsolidadoMaestro).mockResolvedValue({
      document: { attachmentId: "att-1", tipo: "consolidado_maestro", filename: "c.pdf", sha256: "x" },
      regenerado: true,
    });
    vi.mocked(fetchOtAttachmentPreviewUrl).mockResolvedValue({
      url: "https://s3.test/consolidado",
      expiresAt: "2026-07-06T10:10:00Z",
    });
    // El re-empaquetado a Blob usa fetch + URL.createObjectURL del navegador (stub en jsdom).
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({ ok: true, blob: () => Promise.resolve(new Blob(["%PDF"])) }),
    );
    URL.createObjectURL = vi.fn(() => "blob:mock");
    URL.revokeObjectURL = vi.fn();

    const user = userEvent.setup();
    renderSection();
    // No hay botón "Generar consolidado" separado: el único botón es "Ver consolidado".
    expect(screen.queryByRole("button", { name: /Generar consolidado/i })).not.toBeInTheDocument();
    await user.click(await screen.findByRole("button", { name: /Ver consolidado/i }));
    // Asegura el consolidado (idempotente) y luego lo previsualiza inline con el id devuelto.
    // Tercer argumento `force`: en false por el camino normal —el backend decide si regenera por la
    // marca de vigencia. El "Actualizar consolidado" de la sección Documentos lo manda en true.
    await waitFor(() =>
      expect(generarOtConsolidadoMaestro).toHaveBeenCalledWith("proc-1", undefined, false),
    );
    await waitFor(() =>
      expect(fetchOtAttachmentPreviewUrl).toHaveBeenCalledWith("proc-1", "att-1", undefined),
    );
    vi.unstubAllGlobals();
  });

  it("aprobar con LT seleccionada aprueba ANTES de adjuntar la licencia", async () => {
    vi.mocked(adjuntarOtLicenciaTransito).mockResolvedValue({
      // HU #11996 — el backend devuelve el adjunto junto al análisis OCR.
      ocr: null,
      attachment: {
        id: "att-lt",
        tipo: "licencia_transito",
        filename: "lt.pdf",
        mimetype: "application/pdf",
        sizeBytes: 10,
        sha256: "x",
        source: "ot",
        uploadedAt: "2026-07-06T10:00:00Z",
      },
    });
    const user = userEvent.setup();
    renderSection();
    // RowActions expone la acción como botón-icono con aria-label "Aprobar tramite {ref}".
    await user.click(await screen.findByRole("button", { name: /^Aprobar tramite/i }));
    const file = new File(["%PDF-lt"], "lt.pdf", { type: "application/pdf" });
    await user.upload(screen.getByLabelText(/Licencia de Tránsito \(LT\)/i), file);
    await user.click(screen.getByRole("button", { name: /Confirmar$/i }));
    await waitFor(() => expect(adjuntarOtLicenciaTransito).toHaveBeenCalledWith("proc-1", file, undefined, null));
    expect(approveOtClientProcedure).toHaveBeenCalledWith("proc-1", undefined);
    // La aprobación va PRIMERO: el gate de la LT exige el trámite en aprobado (ruta de placa
    // Feature #10587: llega a la aprobación en 'asignado', donde adjuntar antes fallaría).
    const ltOrder = vi.mocked(adjuntarOtLicenciaTransito).mock.invocationCallOrder[0];
    const approveOrder = vi.mocked(approveOtClientProcedure).mock.invocationCallOrder[0];
    expect(approveOrder).toBeLessThan(ltOrder);
  });

  it("fila aprobada ofrece 'Adjuntar LT' para el OT admin", async () => {
    vi.mocked(fetchOtClientProcedures).mockResolvedValue({
      data: [{ ...procedure, status: "aprobado" }],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });
    vi.mocked(adjuntarOtLicenciaTransito).mockResolvedValue({
      // HU #11996 — el backend devuelve el adjunto junto al análisis OCR.
      ocr: null,
      attachment: {
        id: "att-lt",
        tipo: "licencia_transito",
        filename: "lt.pdf",
        mimetype: "application/pdf",
        sizeBytes: 10,
        sha256: "x",
        source: "ot",
        uploadedAt: "2026-07-06T10:00:00Z",
      },
    });
    const user = userEvent.setup();
    renderSection();
    await user.click(await screen.findByRole("button", { name: /Adjuntar LT/i }));
    const dialog = screen.getByRole("dialog", { name: /Adjuntar Licencia de Tránsito/i });
    const file = new File(["%PDF-lt"], "lt.pdf", { type: "application/pdf" });
    await user.upload(within(dialog).getByLabelText(/Archivo de la Licencia de Tránsito/i), file);
    await user.click(within(dialog).getByRole("button", { name: /^Adjuntar LT$/i }));
    await waitFor(() =>
      expect(adjuntarOtLicenciaTransito).toHaveBeenCalledWith("proc-1", file, undefined, null),
    );
  });

  it("HU10541 — muestra banner de diagnóstico cuando hay entregados sin grant", async () => {
    vi.mocked(fetchOtBandejaHealth).mockResolvedValue({
      transitOfficeResolved: true,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      deliveredTotal: 3,
      deliveredWithGrant: 1,
      deliveredWithoutGrant: 2,
      hasDeliveredWithoutGrant: true,
    });
    renderSection();
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(/2\s+trámites entregados sin grant vigente/i);
    expect(alert).toHaveTextContent(/grant OT.?empresa/i);
  });

  it("HU10541 — no muestra el banner cuando la bandeja está sana", async () => {
    renderSection();
    expect(await screen.findByText("RAD-2026-001")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("HU10541 — el diagnóstico se consulta con el scope del SuperAdmin", async () => {
    renderSection("aaaaaaaa-0001-4000-8000-000000000001");
    await waitFor(() =>
      expect(fetchOtBandejaHealth).toHaveBeenCalledWith(expect.anything(), {
        transitOfficeId: "aaaaaaaa-0001-4000-8000-000000000001",
      }),
    );
  });

  // ── HU #12042 — el veredicto ANTES de decidir ──────────────────────────────
  // El defecto que corrige: el OCR corría DESPUÉS de aprobar y se anunciaba en un toast efímero, así
  // que el OT decidía a ciegas. Ahora se analiza al seleccionar y el resultado vive en la modal.

  it("analiza la LT al seleccionarla, sin esperar a que el OT confirme", async () => {
    vi.mocked(tramitesClient.analyzeDocument).mockResolvedValue({
      ok: true,
      tipo: "tarjeta_propiedad",
      data: { es_valido: true, vehiculo_placa: "ABC123" },
    } as never);
    const user = userEvent.setup();
    renderSection();

    await user.click(await screen.findByRole("button", { name: /^Aprobar tramite/i }));
    const file = new File(["%PDF-lt"], "lt.pdf", { type: "application/pdf" });
    await user.upload(screen.getByLabelText(/Licencia de Tránsito \(LT\)/i), file);

    await waitFor(() =>
      expect(tramitesClient.analyzeDocument).toHaveBeenCalledWith("tarjeta_propiedad", file),
    );
    expect(await screen.findByText(/Parece una Licencia de Tránsito/i)).toBeInTheDocument();
    expect(screen.getByText("ABC123")).toBeInTheDocument();
  });

  it("avisa en la modal cuando el documento NO parece una licencia, y deja confirmar igual", async () => {
    vi.mocked(tramitesClient.analyzeDocument).mockResolvedValue({
      ok: true,
      tipo: "tarjeta_propiedad",
      data: { es_valido: false, observaciones: "Es un recibo de derechos de tránsito." },
    } as never);
    const user = userEvent.setup();
    renderSection();

    await user.click(await screen.findByRole("button", { name: /^Aprobar tramite/i }));
    await user.upload(
      screen.getByLabelText(/Licencia de Tránsito \(LT\)/i),
      new File(["%PDF"], "recibo.pdf", { type: "application/pdf" }),
    );

    expect(await screen.findByText(/NO parece una Licencia de Tránsito/i)).toBeInTheDocument();
    expect(screen.getByText(/recibo de derechos de tránsito/i)).toBeInTheDocument();
    // El OCR informa, nunca bloquea: el botón sigue disponible.
    expect(screen.getByRole("button", { name: /Confirmar$/i })).toBeEnabled();
  });

  it("registra el MISMO análisis que se le mostró al OT", async () => {
    // Dos análisis del mismo archivo pueden diferir, así que se manda el que el usuario vio en vez
    // de dejar que el backend lo repita.
    const data = { es_valido: true, vehiculo_placa: "XYZ789" };
    vi.mocked(tramitesClient.analyzeDocument).mockResolvedValue({
      ok: true,
      tipo: "tarjeta_propiedad",
      data,
    } as never);
    vi.mocked(adjuntarOtLicenciaTransito).mockResolvedValue({ ocr: null, attachment: null } as never);
    const user = userEvent.setup();
    renderSection();

    await user.click(await screen.findByRole("button", { name: /^Aprobar tramite/i }));
    const file = new File(["%PDF-lt"], "lt.pdf", { type: "application/pdf" });
    await user.upload(screen.getByLabelText(/Licencia de Tránsito \(LT\)/i), file);
    await screen.findByText(/Parece una Licencia de Tránsito/i);
    await user.click(screen.getByRole("button", { name: /Confirmar$/i }));

    await waitFor(() =>
      expect(adjuntarOtLicenciaTransito).toHaveBeenCalledWith("proc-1", file, undefined, data),
    );
  });

  it("si no se puede analizar, lo dice y permite continuar", async () => {
    vi.mocked(tramitesClient.analyzeDocument).mockRejectedValue(new Error("proveedor caído"));
    const user = userEvent.setup();
    renderSection();

    await user.click(await screen.findByRole("button", { name: /^Aprobar tramite/i }));
    await user.upload(
      screen.getByLabelText(/Licencia de Tránsito \(LT\)/i),
      new File(["%PDF"], "lt.pdf", { type: "application/pdf" }),
    );

    expect(await screen.findByText(/No se pudo verificar el documento/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Confirmar$/i })).toBeEnabled();
  });

});
