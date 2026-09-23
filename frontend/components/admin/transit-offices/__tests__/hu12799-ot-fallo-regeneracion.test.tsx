/**
 * HU #12799 (AC3) — Consola OT: si el maestro falló al regenerarse, el funcionario ve el aviso y la
 * fecha del documento que sí está disponible. Contrato #12798: POST del maestro y ruta de entrega
 * responden con éxito, el PDF anterior, `regenerado: false`, `avisosCascada:
 * ["consolidado_maestro: <causa>"]` y, en la entrega, `modo: null`.
 *
 * Uso de ejemplo:
 *   <OtDetalleDocumentos procedureId={row.id} consolidadoMaestro={row.consolidadoMaestro} />
 */
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import type {
  ConsolidadoVigencia,
  GenerarConsolidadoResult,
} from "@/lib/api/types/procedure-runtime";

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

// 15:05 UTC = 10:05 Bogotá. Datos ficticios.
const CONSERVADO = "2026-09-20T15:05:00Z";
const RADICADO = "2026-09-21T14:30:00Z";

function maestro(overrides: Partial<ConsolidadoVigencia> = {}): ConsolidadoVigencia {
  return {
    estado: "desactualizado",
    generadoEn: CONSERVADO,
    origen: "system",
    definitivo: false,
    modo: null,
    ...overrides,
  };
}

function res(overrides: Partial<GenerarConsolidadoResult> = {}): GenerarConsolidadoResult {
  return {
    document: {
      attachmentId: "att-maestro-anterior",
      tipo: "consolidado_maestro",
      filename: "maestro.pdf",
      sha256: "anterior",
    },
    regenerado: true,
    avisosCascada: [],
    ...overrides,
  };
}

const FALLO_POST = res({
  regenerado: false,
  avisosCascada: ["consolidado_maestro: storage_unavailable"],
});
const FALLO_ENTREGA = res({
  regenerado: false,
  modo: null,
  avisosCascada: ["consolidado_maestro: excepcion"],
});

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(fetchOtDocuments).mockResolvedValue({
    data: [],
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

type Props = Parameters<typeof OtDetalleDocumentos>[0];

function renderDocs(props: Partial<Props> = {}) {
  return render(
    <ToastProvider>
      <OtDetalleDocumentos procedureId="proc-1" consolidadoMaestro={maestro()} {...props} />
    </ToastProvider>,
  );
}

async function abrirConsolidado(user: ReturnType<typeof userEvent.setup>) {
  await waitFor(() => expect(fetchOtDocuments).toHaveBeenCalled());
  const fila = (await screen.findByText("Consolidado de documentos")).closest("li")!;
  await user.click(within(fila).getAllByRole("button")[0]);
}

/** Aviso en el bloque de documentos (fuera del visor modal). */
function avisoEnDocumentos() {
  return within(screen.getByTestId("ot-detalle-documentos")).getByTestId("aviso-fallo-regeneracion");
}

describe("hu12799 AC3 — detalle OT: aviso de fallo del maestro", () => {
  it("POST con fallo: aviso con la fecha del maestro disponible", async () => {
    vi.mocked(generarOtConsolidadoMaestro).mockResolvedValue(FALLO_POST);
    const user = userEvent.setup();
    renderDocs();

    await abrirConsolidado(user);

    await waitFor(() => expect(screen.getAllByTestId("aviso-fallo-regeneracion").length).toBeGreaterThan(0));
    const aviso = avisoEnDocumentos();
    expect(aviso).toHaveAttribute("role", "alert");
    expect(aviso).toHaveTextContent("No se pudo regenerar el consolidado maestro.");
    expect(aviso).toHaveTextContent("es la versión generada el 20/09/2026 10:05 (hora Colombia)");
    expect(aviso.textContent).not.toMatch(/storage_unavailable/);
    // Se previsualiza el PDF anterior servido, con el aviso también sobre el visor.
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByTestId("aviso-fallo-regeneracion")).toBeInTheDocument();
    expect(fetchOtAttachmentPreviewUrl).toHaveBeenCalledWith("proc-1", "att-maestro-anterior", undefined);
  });

  it("un maestro vigente que falla al actualizarse avisa y NO muestra «Consolidado generado»", async () => {
    vi.mocked(generarOtConsolidadoMaestro).mockResolvedValue(FALLO_POST);
    const user = userEvent.setup();
    renderDocs({ consolidadoMaestro: maestro({ estado: "vigente" }) });

    await user.click(await screen.findByRole("button", { name: "Actualizar el consolidado del expediente" }));

    await waitFor(() => expect(screen.getAllByTestId("aviso-fallo-regeneracion").length).toBeGreaterThan(0));
    expect(screen.queryByText("Consolidado generado.")).toBeNull();
  });

  it("read-only no radicado: la entrega con fallo (modo null) también avisa", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(FALLO_ENTREGA);
    const user = userEvent.setup();
    renderDocs({ readOnly: true });

    await abrirConsolidado(user);

    await waitFor(() => expect(screen.getAllByTestId("aviso-fallo-regeneracion").length).toBeGreaterThan(0));
    expect(avisoEnDocumentos()).toHaveTextContent(/error inesperado/);
  });

  it("«Reintentar» reconstruye (force) y, con éxito, el aviso desaparece", async () => {
    vi.mocked(generarOtConsolidadoMaestro)
      .mockResolvedValueOnce(FALLO_POST)
      .mockResolvedValueOnce(res());
    const user = userEvent.setup();
    renderDocs();

    await abrirConsolidado(user);
    await waitFor(() => expect(screen.getAllByTestId("aviso-fallo-regeneracion").length).toBeGreaterThan(0));
    // Cerrar el visor para operar el aviso de la página.
    await user.click(within(await screen.findByRole("dialog")).getAllByRole("button", { name: /cerrar/i })[0]);

    await user.click(
      within(avisoEnDocumentos()).getByRole("button", {
        name: "Reintentar la regeneración del consolidado maestro",
      }),
    );

    await waitFor(() => expect(generarOtConsolidadoMaestro).toHaveBeenCalledTimes(2));
    expect(generarOtConsolidadoMaestro).toHaveBeenLastCalledWith("proc-1", undefined, true);
    await waitFor(() => expect(screen.queryAllByTestId("aviso-fallo-regeneracion")).toHaveLength(0));
  });

  it("AC4 — sin fallo (regenerado) no hay aviso", async () => {
    vi.mocked(generarOtConsolidadoMaestro).mockResolvedValue(res());
    const user = userEvent.setup();
    renderDocs();

    await abrirConsolidado(user);

    await screen.findByRole("dialog");
    await waitFor(() => expect(fetchOtAttachmentPreviewUrl).toHaveBeenCalled());
    expect(screen.queryAllByTestId("aviso-fallo-regeneracion")).toHaveLength(0);
  });

  it("maestro radicado (read-only, fijo): nunca hay aviso de regeneración", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(FALLO_ENTREGA);
    const user = userEvent.setup();
    renderDocs({ readOnly: true, quipuxRadicadoEn: RADICADO });

    await abrirConsolidado(user);

    await waitFor(() => expect(entregarOtConsolidado).toHaveBeenCalled());
    await screen.findByTestId("aviso-maestro-radicado");
    expect(screen.queryAllByTestId("aviso-fallo-regeneracion")).toHaveLength(0);
  });
});

describe("hu12799 AC3 — bandeja OT: el maestro servido con fallo lleva el aviso en el visor", () => {
  const procedure: OtClientProcedure = {
    id: "proc-1",
    clientTenantId: "client-tenant-aaaa",
    clientTenantName: "Cliente de prueba",
    procedureTypeId: "tipo-1",
    procedureTypeName: "Matrícula inicial",
    referenceNumber: "RAD-2026-001",
    status: "entregado",
    createdAt: "2026-09-01T09:00:00Z",
    consolidadoMaestro: maestro(),
  };

  async function abrirDesdeBandeja(readOnly: boolean) {
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "quipux",
      quipuxReadOnly: readOnly,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      featureFlags: [],
      revocationWindowBusinessDays: null,
    });
    vi.mocked(fetchOtBandejaFilterFields).mockResolvedValue([]);
    vi.mocked(searchOtClientProcedures).mockResolvedValue({
      data: [procedure],
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
    await waitFor(() => expect(fetchOtProfile).toHaveBeenCalled());
    await user.click(await screen.findByRole("button", { name: /Acciones del trámite/i }));
    await user.click(await screen.findByRole("menuitem", { name: /Ver consolidado/i }));
    return screen.findByRole("dialog", { name: /Consolidado — RAD-2026-001/ });
  }

  it("read-only: la entrega con fallo muestra el aviso con la fecha del maestro de la fila", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(FALLO_ENTREGA);

    const dialog = await abrirDesdeBandeja(true);

    const aviso = await within(dialog).findByTestId("aviso-fallo-regeneracion");
    expect(aviso).toHaveTextContent("20/09/2026 10:05 (hora Colombia)");
    expect(aviso).toHaveTextContent("No se pudo regenerar el consolidado maestro.");
  });

  it("read-only sin fallo: no hay aviso", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(res({ modo: "regenerado" }));

    const dialog = await abrirDesdeBandeja(true);

    await waitFor(() => expect(fetchOtAttachmentPreviewUrl).toHaveBeenCalled());
    expect(within(dialog).queryByTestId("aviso-fallo-regeneracion")).toBeNull();
  });
});


// Uso de ejemplo (Épica #12760, G3): entrega OT con `modo: "radicado_fijo"` ⇒ versión radicada.
describe("modo radicado_fijo — el backend sirve el maestro radicado tal cual", () => {
  const RADICADO_FIJO = res({
    regenerado: false,
    modo: "radicado_fijo",
    avisosCascada: ["consolidado_maestro: excepcion"],
  });

  it("detalle OT read-only: sin aviso de fallo", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue(RADICADO_FIJO);
    const user = userEvent.setup();
    renderDocs({ readOnly: true });

    await abrirConsolidado(user);

    await waitFor(() => expect(entregarOtConsolidado).toHaveBeenCalled());
    await waitFor(() => expect(fetchOtAttachmentPreviewUrl).toHaveBeenCalled());
    expect(screen.queryAllByTestId("aviso-fallo-regeneracion")).toHaveLength(0);
  });

  it("bandeja OT read-only: la fila radicada sin adjunto conocido muestra la versión radicada, sin aviso de fallo", async () => {
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "quipux",
      quipuxReadOnly: true,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      featureFlags: [],
      revocationWindowBusinessDays: null,
    });
    vi.mocked(fetchOtBandejaFilterFields).mockResolvedValue([]);
    vi.mocked(searchOtClientProcedures).mockResolvedValue({
      data: [
        {
          id: "proc-1",
          clientTenantId: "client-tenant-aaaa",
          clientTenantName: "Cliente de prueba",
          procedureTypeId: "tipo-1",
          procedureTypeName: "Matrícula inicial",
          referenceNumber: "RAD-2026-002",
          status: "entregado",
          createdAt: "2026-09-01T09:00:00Z",
          consolidadoMaestro: maestro(),
          quipuxRadicadoEn: RADICADO,
        } as OtClientProcedure,
      ],
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
    vi.mocked(entregarOtConsolidado).mockResolvedValue(RADICADO_FIJO);
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <ClientProceduresSection />
      </ToastProvider>,
    );
    await waitFor(() => expect(fetchOtProfile).toHaveBeenCalled());
    await user.click(await screen.findByRole("button", { name: /Acciones del trámite/i }));
    await user.click(await screen.findByRole("menuitem", { name: /Ver consolidado/i }));
    const dialog = await screen.findByRole("dialog", { name: /Consolidado — RAD-2026-002/ });

    expect(await within(dialog).findByTestId("aviso-maestro-radicado")).toBeInTheDocument();
    expect(within(dialog).queryByTestId("aviso-fallo-regeneracion")).toBeNull();
  });
});
