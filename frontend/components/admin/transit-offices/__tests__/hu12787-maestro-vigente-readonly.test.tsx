/**
 * HU #12787 — [FRONTEND] Consola OT: servir el maestro VIGENTE en el detalle read-only.
 *
 * En modo Quipux read-only la consola dejaba de tomar el maestro del adjunto de `GET …/documents`
 * (que puede estar desactualizado) y lo pide a la ruta de entrega OT (HU #12785):
 * `GET /admin/ot/client-procedures/{id}/consolidado/entrega`.
 *
 * Uso de ejemplo:
 *   const res = await entregarOtConsolidado(procId, scope, { tipo: 'consolidado_maestro' });
 *   // → previsualizar res.document.attachmentId; esDocumentoDefinitivo(res) → aviso de final
 */
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ApiError } from "@/lib/api/types";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import type { ConsolidadoEntregaResult } from "@/lib/api/types/procedure-runtime";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedures: vi.fn(),
  searchOtClientProcedures: vi.fn(),
  fetchOtBandejaFilterFields: vi.fn(),
  fetchOtBandejaHealth: vi.fn(),
  fetchOtProfile: vi.fn(),
  approveOtClientProcedure: vi.fn(),
  rejectOtClientProcedure: vi.fn(),
  generarOtConsolidadoMaestro: vi.fn(),
  entregarOtConsolidado: vi.fn(),
  fetchOtDocuments: vi.fn(),
  fetchOtAttachmentPreviewUrl: vi.fn(),
  adjuntarOtLicenciaTransito: vi.fn(),
}));

vi.mock("@/lib/api/download", () => ({ downloadFile: vi.fn() }));

vi.mock("@/lib/auth/jwt", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/auth/jwt")>();
  return { ...actual, isSuperAdmin: () => false };
});

vi.mock("@/lib/api/admin-mandate-signers", () => ({ fetchMandateSigners: vi.fn() }));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    analyzeDocument: vi.fn(),
    listPublishedProcedureTypes: vi.fn().mockResolvedValue([]),
  },
}));

import {
  entregarOtConsolidado,
  fetchOtAttachmentPreviewUrl,
  fetchOtBandejaFilterFields,
  fetchOtBandejaHealth,
  fetchOtDocuments,
  fetchOtProfile,
  generarOtConsolidadoMaestro,
  searchOtClientProcedures,
} from "@/lib/api/admin-ot";
import { OtDetalleDocumentos } from "../detalle/OtDetalleDocumentos";
import { ClientProceduresSection } from "../ClientProceduresSection";

// ── Fixtures (datos ficticios) ──────────────────────────────────────
const MAESTRO_VIEJO = {
  id: "att-maestro-viejo",
  tipo: "consolidado_maestro",
  filename: "maestro.pdf",
  mimetype: "application/pdf",
  sizeBytes: 2048,
  sha256: "viejo",
  source: "system",
  uploadedAt: "2026-09-01T00:00:00Z",
};

function entrega(overrides: Partial<ConsolidadoEntregaResult> = {}): ConsolidadoEntregaResult {
  return {
    document: {
      attachmentId: "att-maestro-vigente",
      tipo: "consolidado_maestro",
      filename: "maestro-vigente.pdf",
      sha256: "vigente",
    },
    regenerado: true,
    definitivoPorEstadoFinal: false,
    modo: "regenerado",
    ...overrides,
  };
}

const procedure: OtClientProcedure = {
  id: "proc-1",
  clientTenantId: "client-tenant-aaaa",
  clientTenantName: "Cliente de prueba",
  procedureTypeId: "tipo-1",
  procedureTypeName: "Matrícula inicial",
  referenceNumber: "RAD-2026-001",
  status: "entregado",
  createdAt: "2026-09-01T09:00:00Z",
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(fetchOtDocuments).mockResolvedValue({
    data: [MAESTRO_VIEJO],
    consolidado: false,
    consolidado_maestro: true,
  });
  vi.mocked(fetchOtAttachmentPreviewUrl).mockResolvedValue({
    url: "https://s3.test/maestro.pdf",
    expiresAt: "2026-09-23T10:10:00Z",
  });
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue({ ok: true, blob: () => Promise.resolve(new Blob(["%PDF"])) }),
  );
  URL.createObjectURL = vi.fn(() => "blob:maestro");
  URL.revokeObjectURL = vi.fn();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function renderDocumentos(
  readOnly: boolean,
  radicacion: { quipuxRadicadoEn?: string | null; quipuxMaestroAttachmentId?: string | null } = {},
) {
  return render(
    <ToastProvider>
      <OtDetalleDocumentos procedureId="proc-1" readOnly={readOnly} {...radicacion} />
    </ToastProvider>,
  );
}

async function renderBandejaReadOnly(row: OtClientProcedure = procedure) {
  vi.mocked(fetchOtProfile).mockResolvedValue({
    operationMode: "quipux",
    quipuxReadOnly: true,
    transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    featureFlags: [],
    revocationWindowBusinessDays: null,
  });
  vi.mocked(fetchOtBandejaFilterFields).mockResolvedValue([]);
  vi.mocked(searchOtClientProcedures).mockResolvedValue({
    data: [row],
    totalCount: 1,
    page: 1,
    pageSize: 20,
  });
  vi.mocked(fetchOtBandejaHealth).mockResolvedValue({
    transitOfficeResolved: true,
    transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    deliveredTotal: 1,
    deliveredWithGrant: 1,
    deliveredWithoutGrant: 0,
    hasDeliveredWithoutGrant: false,
  });
  const user = userEvent.setup();
  render(
    <ToastProvider>
      <ClientProceduresSection />
    </ToastProvider>,
  );
  // El perfil read-only tiene que estar cargado antes de pulsar: `isReadOnly` sale de él.
  await waitFor(() => expect(fetchOtProfile).toHaveBeenCalled());
  await user.click(await screen.findByRole("button", { name: /Acciones del trámite/i }));
  await user.click(await screen.findByRole("menuitem", { name: /Ver consolidado/i }));
  return screen.findByRole("dialog", { name: /Consolidado — RAD-2026-001/ });
}

// ── AC1 ─────────────────────────────────────────────────────────────
describe("HU #12787 AC1 — read-only aún no radicado: el maestro sale de la ruta de entrega OT", () => {
  it("detalle: «Ver consolidado» pide la entrega y previsualiza el adjunto devuelto, no el de /documents", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(entrega());
    renderDocumentos(true);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Ver consolidado/i }));

    await waitFor(() =>
      expect(entregarOtConsolidado).toHaveBeenCalledWith("proc-1", undefined, {
        tipo: "consolidado_maestro",
      }),
    );
    await waitFor(() =>
      expect(fetchOtAttachmentPreviewUrl).toHaveBeenCalledWith(
        "proc-1",
        "att-maestro-vigente",
        undefined,
      ),
    );
    expect(fetchOtAttachmentPreviewUrl).not.toHaveBeenCalledWith(
      "proc-1",
      "att-maestro-viejo",
      undefined,
    );
    // En read-only NUNCA se llama a la generación (POST): la vigencia la garantiza la entrega.
    expect(generarOtConsolidadoMaestro).not.toHaveBeenCalled();
  });

  it("detalle: previsualizar la FILA del maestro en read-only también pasa por la entrega", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(entrega());
    renderDocumentos(true);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Previsualizar maestro.pdf" }));

    await waitFor(() => expect(entregarOtConsolidado).toHaveBeenCalledTimes(1));
    await waitFor(() =>
      expect(fetchOtAttachmentPreviewUrl).toHaveBeenCalledWith(
        "proc-1",
        "att-maestro-vigente",
        undefined,
      ),
    );
  });

  it("detalle editable (no read-only) conserva la generación con vigencia (POST), sin la entrega", async () => {
    vi.mocked(generarOtConsolidadoMaestro).mockResolvedValue({ ...entrega(), modo: null });
    renderDocumentos(false);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Ver consolidado/i }));

    await waitFor(() =>
      expect(generarOtConsolidadoMaestro).toHaveBeenCalledWith("proc-1", undefined, false),
    );
    expect(entregarOtConsolidado).not.toHaveBeenCalled();
  });

  it("bandeja read-only: «Ver consolidado» pide la entrega, no la lista de documentos", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(entrega());

    await renderBandejaReadOnly();

    await waitFor(() =>
      expect(entregarOtConsolidado).toHaveBeenCalledWith("proc-1", undefined, {
        tipo: "consolidado_maestro",
      }),
    );
    await waitFor(() =>
      expect(fetchOtAttachmentPreviewUrl).toHaveBeenCalledWith(
        "proc-1",
        "att-maestro-vigente",
        undefined,
      ),
    );
    expect(fetchOtDocuments).not.toHaveBeenCalled();
    expect(generarOtConsolidadoMaestro).not.toHaveBeenCalled();
  });

  it("bandeja read-only: 404 de la entrega conserva el aviso «aún no tiene consolidado generado»", async () => {
    vi.mocked(entregarOtConsolidado).mockRejectedValue(
      new ApiError(404, "consolidado_no_generado", { error: "consolidado_no_generado" }),
    );

    const dialog = await renderBandejaReadOnly();

    expect(
      await within(dialog).findByText("El trámite aún no tiene consolidado generado."),
    ).toBeInTheDocument();
    expect(fetchOtAttachmentPreviewUrl).not.toHaveBeenCalled();
  });

  it("detalle read-only: un fallo técnico de la entrega avisa sin caer al adjunto viejo", async () => {
    vi.mocked(entregarOtConsolidado).mockRejectedValue(new ApiError(503, "storage_unavailable"));
    renderDocumentos(true);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Ver consolidado/i }));

    expect(
      await screen.findByText("No se pudo abrir el consolidado. Intenta de nuevo."),
    ).toBeInTheDocument();
    expect(fetchOtAttachmentPreviewUrl).not.toHaveBeenCalled();
  });
});

// ── AC3 ─────────────────────────────────────────────────────────────
describe("HU #12787 AC3 — expediente aprobado o rechazado: documento definitivo sin regenerar", () => {
  it.each([
    ["aprobado", "definitivo_estado_final"],
    ["rechazado (anulado)", "definitivo_estado_final"],
  ] as const)("detalle read-only (%s): muestra el aviso de documento final", async (_estado, modo) => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(
      entrega({ regenerado: false, definitivoPorEstadoFinal: true, modo }),
    );
    renderDocumentos(true);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Ver consolidado/i }));

    const aviso = await screen.findByTestId("aviso-documento-final");
    expect(aviso).toHaveTextContent("Documento final.");
    // Sin force ni generación: la entrega sirve el definitivo tal cual.
    expect(entregarOtConsolidado).toHaveBeenCalledWith("proc-1", undefined, {
      tipo: "consolidado_maestro",
    });
    expect(generarOtConsolidadoMaestro).not.toHaveBeenCalled();
  });

  it("bandeja read-only: el definitivo lleva el aviso en el visor", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(
      entrega({ regenerado: false, definitivoPorEstadoFinal: true, modo: "definitivo_estado_final" }),
    );

    const dialog = await renderBandejaReadOnly();

    expect(await within(dialog).findByTestId("aviso-documento-final")).toBeInTheDocument();
  });

  it("un maestro vigente NO lleva el aviso de documento final", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(
      entrega({ regenerado: false, modo: "vigente" }),
    );
    renderDocumentos(true);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Ver consolidado/i }));

    await waitFor(() => expect(fetchOtAttachmentPreviewUrl).toHaveBeenCalled());
    expect(screen.queryByTestId("aviso-documento-final")).toBeNull();
  });
});

// ── AC2 ─────────────────────────────────────────────────────────────
// Decisión del usuario «maestro radicado, fijo»: si el trámite ya se radicó en Quipux, la vista de
// solo lectura sirve ESE maestro tal cual y no lo regenera.
const RADICADO_EN = "2026-09-20T15:30:00Z"; // 20/09/2026 10:30 en hora Colombia
const FECHA_COLOMBIA = "20/09/2026 10:30";

describe("HU #12787 AC2 — trámite ya radicado en Quipux: se sirve el maestro radicado, fijo", () => {
  it("detalle con adjunto radicado: previsualiza ese adjunto por /documents, sin la entrega, con aviso", async () => {
    renderDocumentos(true, {
      quipuxRadicadoEn: RADICADO_EN,
      quipuxMaestroAttachmentId: "att-maestro-radicado",
    });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Ver consolidado/i }));

    await waitFor(() =>
      expect(fetchOtAttachmentPreviewUrl).toHaveBeenCalledWith(
        "proc-1",
        "att-maestro-radicado",
        undefined,
      ),
    );
    // Ni la entrega (que podría regenerar) ni la generación.
    expect(entregarOtConsolidado).not.toHaveBeenCalled();
    expect(generarOtConsolidadoMaestro).not.toHaveBeenCalled();
    const aviso = await screen.findByTestId("aviso-maestro-radicado");
    expect(aviso).toHaveAttribute("role", "status");
    expect(aviso).toHaveTextContent("Versión radicada en Quipux.");
    expect(aviso).toHaveTextContent(`Radicado el ${FECHA_COLOMBIA} (hora Colombia)`);
  });

  it("detalle radicado SIN adjunto: cae a la entrega con soloLectura=true y muestra el aviso", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(
      entrega({ regenerado: false, modo: "solo_lectura" }),
    );
    renderDocumentos(true, { quipuxRadicadoEn: RADICADO_EN, quipuxMaestroAttachmentId: null });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Ver consolidado/i }));

    await waitFor(() =>
      expect(entregarOtConsolidado).toHaveBeenCalledWith("proc-1", undefined, {
        tipo: "consolidado_maestro",
        soloLectura: true,
      }),
    );
    await waitFor(() =>
      expect(fetchOtAttachmentPreviewUrl).toHaveBeenCalledWith(
        "proc-1",
        "att-maestro-vigente",
        undefined,
      ),
    );
    expect(generarOtConsolidadoMaestro).not.toHaveBeenCalled();
    expect(await screen.findByTestId("aviso-maestro-radicado")).toHaveTextContent(FECHA_COLOMBIA);
  });

  it("detalle radicado: la FILA del maestro también sirve el adjunto radicado (no el de /documents)", async () => {
    renderDocumentos(true, {
      quipuxRadicadoEn: RADICADO_EN,
      quipuxMaestroAttachmentId: "att-maestro-radicado",
    });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Previsualizar maestro.pdf" }));

    await waitFor(() =>
      expect(fetchOtAttachmentPreviewUrl).toHaveBeenCalledWith(
        "proc-1",
        "att-maestro-radicado",
        undefined,
      ),
    );
    expect(entregarOtConsolidado).not.toHaveBeenCalled();
  });

  it("detalle radicado + estado final por soloLectura: muestra ambos avisos", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(
      entrega({ regenerado: false, definitivoPorEstadoFinal: true, modo: "definitivo_estado_final" }),
    );
    renderDocumentos(true, { quipuxRadicadoEn: RADICADO_EN });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Ver consolidado/i }));

    expect(await screen.findByTestId("aviso-maestro-radicado")).toBeInTheDocument();
    expect(await screen.findByTestId("aviso-documento-final")).toBeInTheDocument();
  });

  it("detalle NO radicado: sin aviso de radicación (flujo AC1)", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(entrega());
    renderDocumentos(true, { quipuxRadicadoEn: null, quipuxMaestroAttachmentId: null });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Ver consolidado/i }));

    await waitFor(() => expect(fetchOtAttachmentPreviewUrl).toHaveBeenCalled());
    expect(entregarOtConsolidado).toHaveBeenCalledWith("proc-1", undefined, {
      tipo: "consolidado_maestro",
    });
    expect(screen.queryByTestId("aviso-maestro-radicado")).toBeNull();
  });

  it("bandeja read-only radicada con adjunto: previsualiza el adjunto radicado sin la entrega", async () => {
    const dialog = await renderBandejaReadOnly({
      ...procedure,
      quipuxRadicadoEn: RADICADO_EN,
      quipuxMaestroAttachmentId: "att-maestro-radicado",
    });

    await waitFor(() =>
      expect(fetchOtAttachmentPreviewUrl).toHaveBeenCalledWith(
        "proc-1",
        "att-maestro-radicado",
        undefined,
      ),
    );
    expect(entregarOtConsolidado).not.toHaveBeenCalled();
    expect(generarOtConsolidadoMaestro).not.toHaveBeenCalled();
    expect(await within(dialog).findByTestId("aviso-maestro-radicado")).toHaveTextContent(
      FECHA_COLOMBIA,
    );
  });

  it("bandeja read-only radicada SIN adjunto: entrega con soloLectura=true", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(
      entrega({ regenerado: false, modo: "solo_lectura" }),
    );
    const dialog = await renderBandejaReadOnly({
      ...procedure,
      quipuxRadicadoEn: RADICADO_EN,
      quipuxMaestroAttachmentId: null,
    });

    await waitFor(() =>
      expect(entregarOtConsolidado).toHaveBeenCalledWith("proc-1", undefined, {
        tipo: "consolidado_maestro",
        soloLectura: true,
      }),
    );
    expect(await within(dialog).findByTestId("aviso-maestro-radicado")).toBeInTheDocument();
    expect(generarOtConsolidadoMaestro).not.toHaveBeenCalled();
  });
});
