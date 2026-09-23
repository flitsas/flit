/**
 * HU #12793 (Épica #12760) — Consola OT: el funcionario ve el indicador de vigencia del
 * consolidado maestro en el detalle de documentos del trámite.
 *
 * Uso de ejemplo:
 *   <OtDetalleDocumentos procedureId={row.id} readOnly={readOnly}
 *     consolidadoMaestro={row.consolidadoMaestro} quipuxRadicadoEn={row.quipuxRadicadoEn} />
 */
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import type { ConsolidadoVigencia } from "@/lib/api/types/procedure-runtime";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedure: vi.fn(),
  generarOtConsolidadoMaestro: vi.fn(),
  entregarOtConsolidado: vi.fn(),
  fetchOtDocuments: vi.fn(),
  fetchOtAttachmentPreviewUrl: vi.fn(),
}));

vi.mock("@/lib/api/download", () => ({ downloadFile: vi.fn() }));

import {
  entregarOtConsolidado,
  fetchOtAttachmentPreviewUrl,
  fetchOtClientProcedure,
  fetchOtDocuments,
  generarOtConsolidadoMaestro,
} from "@/lib/api/admin-ot";
import { OtDetalleDocumentos } from "../detalle/OtDetalleDocumentos";
import { ClientProcedureDetailModal } from "../ClientProcedureDetailModal";

// 15:05 UTC = 10:05 Bogotá; 14:30 UTC = 09:30 Bogotá. Datos ficticios.
const GENERADO = "2026-09-23T15:05:00Z";
const RADICADO = "2026-09-20T14:30:00Z";

function maestro(overrides: Partial<ConsolidadoVigencia> = {}): ConsolidadoVigencia {
  return {
    estado: "vigente",
    generadoEn: GENERADO,
    origen: "system",
    definitivo: false,
    modo: null,
    ...overrides,
  };
}

const DOCUMENTO = {
  attachmentId: "att-maestro",
  tipo: "consolidado_maestro",
  filename: "maestro.pdf",
  sha256: "abc",
};

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
      <OtDetalleDocumentos procedureId="proc-1" {...props} />
    </ToastProvider>,
  );
}

/** Espera a que `GET …/documents` resuelva (evita avisos de act al terminar el test). */
async function asentar() {
  await screen.findByText("Consolidado de documentos");
}

function indicador() {
  return screen.getByTestId("vigencia-consolidado");
}

/** Botón «ver» de la tarjeta azul del consolidado (el primero de su fila). */
async function abrirConsolidado() {
  await waitFor(() => expect(fetchOtDocuments).toHaveBeenCalled());
  const fila = (await screen.findByText("Consolidado de documentos")).closest("li")!;
  await userEvent.click(within(fila).getAllByRole("button")[0]);
}

describe("HU #12793 AC1 — maestro vigente en el detalle del OT", () => {
  it("indicador verde con la fecha y hora de generación del maestro", async () => {
    renderDocs({ consolidadoMaestro: maestro() });
    await asentar();
    expect(indicador()).toHaveAttribute("data-estado", "vigente");
    expect(indicador()).toHaveAttribute("data-variante", "completa");
    const estado = screen.getByRole("group", { name: /consolidado maestro: vigente/i });
    expect(within(estado).getByText("Consolidado maestro: Vigente")).toBeInTheDocument();
    expect(within(estado).getByText("Generado el 23/09/2026 10:05")).toBeInTheDocument();
    expect(within(estado).getByTestId("vigencia-consolidado-punto").style.background).toMatch(
      /rgb\(112, 207, 58\)|#70CF3A/i,
    );
  });

  it("se ve aunque la lista de documentos siga cargando", () => {
    vi.mocked(fetchOtDocuments).mockReturnValue(new Promise(() => {}));
    renderDocs({ consolidadoMaestro: maestro() });
    expect(indicador()).toBeInTheDocument();
  });

  it("sin vigencia del backend (null) y sin radicación ⇒ no pinta indicador", async () => {
    renderDocs({ consolidadoMaestro: null });
    await asentar();
    expect(screen.queryByTestId("vigencia-consolidado")).not.toBeInTheDocument();
  });

  it("tras reconstruir el maestro, el indicador se refresca a vigente", async () => {
    vi.mocked(generarOtConsolidadoMaestro).mockResolvedValue({
      document: DOCUMENTO,
      regenerado: true,
    });
    renderDocs({ consolidadoMaestro: maestro({ estado: "desactualizado" }) });
    expect(indicador()).toHaveAttribute("data-estado", "desactualizado");
    await waitFor(() => expect(fetchOtDocuments).toHaveBeenCalled());
    await userEvent.click(
      await screen.findByRole("button", { name: "Actualizar el consolidado del expediente" }),
    );
    await waitFor(() => expect(indicador()).toHaveAttribute("data-estado", "vigente"));
    expect(generarOtConsolidadoMaestro).toHaveBeenCalledWith("proc-1", undefined, true);
  });
});

describe("HU #12793 AC2 — maestro desactualizado", () => {
  it("indicador gris con la advertencia de que se reconstruirá al abrirlo", async () => {
    renderDocs({ consolidadoMaestro: maestro({ estado: "desactualizado" }) });
    await asentar();
    expect(indicador()).toHaveAttribute("data-estado", "desactualizado");
    const estado = screen.getByRole("group", {
      name: /desactualizado, se reconstruirá al abrirlo/i,
    });
    expect(within(estado).getByText("Consolidado maestro: Desactualizado")).toBeInTheDocument();
    expect(
      within(estado).getByText(
        /Se reconstruirá al abrirlo · Última generación: 23\/09\/2026 10:05/,
      ),
    ).toBeInTheDocument();
    expect(within(estado).getByTestId("vigencia-consolidado-punto").style.background).toMatch(
      /rgb\(89, 103, 125\)|#59677D/i,
    );
  });

  it("en read-only NO radicado también avisa que se reconstruirá (#12787 AC1)", async () => {
    renderDocs({ readOnly: true, consolidadoMaestro: maestro({ estado: "desactualizado" }) });
    await asentar();
    expect(indicador()).toHaveAttribute("data-estado", "desactualizado");
    expect(screen.getByText(/Se reconstruirá al abrirlo/)).toBeInTheDocument();
  });

  it("abrir en read-only con entrega regenerada refresca el indicador a vigente", async () => {
    vi.mocked(entregarOtConsolidado).mockResolvedValue({
      document: DOCUMENTO,
      regenerado: true,
      definitivoPorEstadoFinal: false,
      modo: "regenerado",
    });
    renderDocs({ readOnly: true, consolidadoMaestro: maestro({ estado: "desactualizado" }) });
    await abrirConsolidado();
    await waitFor(() => expect(indicador()).toHaveAttribute("data-estado", "vigente"));
  });
});

describe("HU #12793 AC3 — versión radicada en read-only", () => {
  const radicado: Partial<Props> = {
    readOnly: true,
    quipuxRadicadoEn: RADICADO,
    quipuxMaestroAttachmentId: "att-radicado",
    consolidadoMaestro: maestro({ estado: "desactualizado" }),
  };

  it("dice versión radicada, con su fecha, sin sugerir regenerar (aunque figure desactualizado)", async () => {
    renderDocs(radicado);
    await asentar();
    expect(indicador()).toHaveAttribute("data-estado", "radicado");
    const estado = screen.getByRole("group", { name: /consolidado maestro: versión radicada/i });
    expect(within(estado).getByText("Consolidado maestro: Versión radicada")).toBeInTheDocument();
    expect(within(estado).getByText("Radicado el 20/09/2026 09:30")).toBeInTheDocument();
    expect(estado.textContent).not.toMatch(/regener|reconstru/i);
    expect(estado.getAttribute("aria-label")).not.toMatch(/regener|reconstru/i);
    expect(
      screen.queryByRole("button", { name: "Actualizar el consolidado del expediente" }),
    ).not.toBeInTheDocument();
  });

  it("se pinta aunque el backend aún no exponga la vigencia del maestro", async () => {
    renderDocs({ readOnly: true, quipuxRadicadoEn: RADICADO, consolidadoMaestro: null });
    await asentar();
    expect(indicador()).toHaveAttribute("data-estado", "radicado");
  });

  it("fuera de read-only la radicación no fija el indicador (sigue la vigencia)", async () => {
    renderDocs({ ...radicado, readOnly: false });
    await asentar();
    expect(indicador()).toHaveAttribute("data-estado", "desactualizado");
  });

  it("abrir el maestro radicado no cambia el indicador y el aviso largo solo aparece en el visor", async () => {
    renderDocs(radicado);
    expect(screen.queryByTestId("aviso-maestro-radicado")).not.toBeInTheDocument();
    await abrirConsolidado();
    expect(await screen.findByTestId("aviso-maestro-radicado")).toBeInTheDocument();
    expect(entregarOtConsolidado).not.toHaveBeenCalled();
    expect(indicador()).toHaveAttribute("data-estado", "radicado");
  });
});

describe("HU #12793 — contrato: el modal del OT pasa la vigencia del maestro al detalle", () => {
  it("ClientProcedureDetailModal entrega row.consolidadoMaestro a OtDetalleDocumentos", async () => {
    const row: OtClientProcedure = {
      id: "proc-1",
      clientTenantId: "tenant-1",
      procedureTypeId: "tipo-1",
      procedureTypeName: "Traspaso",
      clientTenantName: "Empresa Demo",
      referenceNumber: "RAD-0001",
      status: "entregado",
      createdAt: "2026-08-01T00:00:00Z",
      consolidadoMaestro: maestro(),
    };
    vi.mocked(fetchOtClientProcedure).mockResolvedValue(row);
    render(
      <ToastProvider>
        <ClientProcedureDetailModal
          open
          procedure={row}
          onClose={vi.fn()}
          initialSection="documentos"
        />
      </ToastProvider>,
    );
    await screen.findByTestId("ot-detalle-documentos");
    expect(await screen.findByTestId("vigencia-consolidado")).toHaveAttribute(
      "data-estado",
      "vigente",
    );
    expect(screen.getByText("Generado el 23/09/2026 10:05")).toBeInTheDocument();
  });
});
