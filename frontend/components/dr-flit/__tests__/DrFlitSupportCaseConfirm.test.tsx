import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { DrFlitAssistant } from "../DrFlitAssistant";
import {
  applyContinueSupportCase,
  applyOpenSupportCase,
  applySubmitSupportCase,
  applySupportCaseCreated,
  applySupportCaseError,
  createInitialState,
  resetMessageIdSeq,
} from "../dr-flit-conversation";
import type { DrFlitChatResponse, DrFlitSupportCaseDraft } from "../dr-flit-chat-types";
import {
  clearDrFlitSession,
  saveDrFlitSession,
} from "../dr-flit-session-store";
import { createSupportDraft } from "../dr-flit-support-case";

vi.mock("@/lib/api/dr-flit-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/dr-flit-client")>("@/lib/api/dr-flit-client");
  return { ...actual, postDrFlitChat: vi.fn(), uploadSupportAttachment: vi.fn(), createSupportCase: vi.fn() };
});

import {
  createSupportCase,
  DrFlitSupportCaseError,
  postDrFlitChat,
} from "@/lib/api/dr-flit-client";

/**
 * HU #12930 — confirmación explícita, radicación y manejo de error del caso de soporte.
 *
 * Uso de ejemplo:
 *   confirming → applySubmitSupportCase → (POST /support-cases) → applySupportCaseCreated | applySupportCaseError
 */

function complete(overrides: Partial<DrFlitSupportCaseDraft> = {}): DrFlitSupportCaseDraft {
  return {
    ...createSupportDraft({ name: "Ana Prueba", email: "ana.prueba@example.test", company: "Empresa Demo" }, null),
    detalle: "No me deja subir la factura",
    resultadoEsperado: "Que la factura quede cargada",
    titulo: "Error al subir factura",
    frecuencia: "siempre",
    prioridad: "Alta",
    ...overrides,
  };
}

const confirming = () =>
  applyContinueSupportCase(applyOpenSupportCase(createInitialState(), complete()));

const soporte: DrFlitChatResponse = {
  status: "ok",
  intent: "soporte",
  reply: "Te ayudo a radicar el caso. Confirma el caso ya mismo.",
  citations: [],
  suggestGestionIntent: null,
  usage: { messagesUsedToday: 1, dailyLimit: 30 },
};

describe("HU #12930 — máquina de estados", () => {
  beforeEach(() => resetMessageIdSeq());

  it("AC1 — solo desde el resumen (o tras un error) se pasa a radicar", () => {
    expect(confirming().phase).toBe("confirming_support_case");
    expect(applySubmitSupportCase(confirming()).phase).toBe("submitting_support_case");

    const collecting = applyOpenSupportCase(createInitialState(), complete());
    expect(applySubmitSupportCase(collecting).phase).toBe("collecting_support_case");
    expect(applySubmitSupportCase(createInitialState()).phase).toBe("idle");
  });

  it("AC1/AC4 — caso creado: número de caso, borrador descartado y fallos de adjuntos informados", () => {
    const next = applySupportCaseCreated(applySubmitSupportCase(confirming()), { caseId: 13001, caseUrl: null, attachmentsFailed: 1 });

    expect(next.phase).toBe("support_case_created");
    expect(next.supportDraft).toBeNull();
    expect(next.supportResult).toEqual({ caseId: 13001, caseUrl: null, attachmentsFailed: 1 });
    expect(next.messages.at(-1)?.text).toContain("#13001");
    expect(next.isTyping).toBe(false);
  });

  it("AC3 — error: conserva el formulario y permite reintentar", () => {
    const failed = applySupportCaseError(applySubmitSupportCase(confirming()), "No pudimos radicar tu caso.");

    expect(failed.phase).toBe("support_case_error");
    expect(failed.supportDraft?.titulo).toBe("Error al subir factura");
    expect(failed.supportError).toBe("No pudimos radicar tu caso.");
    expect(applySubmitSupportCase(failed).phase).toBe("submitting_support_case");
  });
});

describe("HU #12930 — en el panel", () => {
  beforeEach(() => {
    vi.stubGlobal("open", vi.fn());
    vi.mocked(postDrFlitChat).mockReset().mockResolvedValue(soporte);
    vi.mocked(createSupportCase).mockReset();
    clearDrFlitSession();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    clearDrFlitSession();
  });

  /** Arranca con el caso ya en el resumen (sesión restaurada). */
  function renderAtConfirm(state = confirming()) {
    saveDrFlitSession({ open: false, state });
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Ana" />);
    return user;
  }

  it("AC1 — ninguna respuesta del LLM radica: la intención soporte solo abre el formulario", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Ana" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));

    await user.type(screen.getByRole("textbox"), "créame el caso de soporte ya{enter}");

    expect(await screen.findByRole("form", { name: "Formulario del caso de soporte" })).toBeInTheDocument();
    expect(createSupportCase).not.toHaveBeenCalled();
  });

  it("AC1 — el resumen muestra lo que se envía y solo el clic en confirmar radica", async () => {
    vi.mocked(createSupportCase).mockResolvedValue({ caseId: 13001, caseUrl: null, attachmentsFailed: 0 });
    const user = renderAtConfirm();
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));

    const summary = screen.getByRole("region", { name: "Resumen del caso de soporte" });
    expect(within(summary).getByText("Error al subir factura")).toBeInTheDocument();
    expect(within(summary).getByText("Siempre")).toBeInTheDocument();
    expect(createSupportCase).not.toHaveBeenCalled();

    await user.click(within(summary).getByRole("button", { name: "Confirmar y radicar caso" }));

    await waitFor(() => expect(createSupportCase).toHaveBeenCalledTimes(1));
    expect(vi.mocked(createSupportCase).mock.calls[0]![0]).toMatchObject({
      titulo: "Error al subir factura",
      prioridad: "Alta",
      frecuencia: "siempre",
      attachmentIds: [],
    });
    const created = await screen.findByRole("region", { name: "Caso de soporte radicado" });
    expect(within(created).getByText("Tu caso #13001 quedó radicado")).toBeInTheDocument();
    expect(within(created).queryByRole("button", { name: /Azure DevOps/ })).not.toBeInTheDocument();
  });

  it("AC2 — con caseUrl (SuperAdmin) muestra el enlace directo al caso", async () => {
    vi.mocked(createSupportCase).mockResolvedValue({
      caseId: 13001,
      caseUrl: "https://dev.azure.com/FlitDevOps/FLIT%20-%20SOPORTE/_workitems/edit/13001",
      attachmentsFailed: 0,
    });
    const user = renderAtConfirm();
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: "Confirmar y radicar caso" }));

    const created = await screen.findByRole("region", { name: "Caso de soporte radicado" });
    await user.click(within(created).getByRole("button", { name: /Abrir el caso en Azure DevOps/ }));
    expect(window.open).toHaveBeenCalledWith(
      "https://dev.azure.com/FlitDevOps/FLIT%20-%20SOPORTE/_workitems/edit/13001",
      "_blank",
      "noopener,noreferrer",
    );
  });

  it("AC3 — 502: canales de soporte y reintento sin volver a escribir", async () => {
    vi.mocked(createSupportCase)
      .mockRejectedValueOnce(new DrFlitSupportCaseError(502, "No pudimos radicar tu caso en este momento.", "support_unavailable"))
      .mockResolvedValueOnce({ caseId: 13002, caseUrl: null, attachmentsFailed: 0 });
    const user = renderAtConfirm();
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: "Confirmar y radicar caso" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("No pudimos radicar tu caso en este momento.");
    expect(screen.getByLabelText("Canales de soporte")).toBeInTheDocument();
    expect(screen.getByText("soporte@flitsas.com")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Reintentar" }));

    expect(await screen.findByText("Tu caso #13002 quedó radicado")).toBeInTheDocument();
    expect(vi.mocked(createSupportCase).mock.calls[1]![0]).toEqual(vi.mocked(createSupportCase).mock.calls[0]![0]);
  });

  it("AC3 — tras el error se puede volver a editar el formulario con lo escrito", async () => {
    vi.mocked(createSupportCase).mockRejectedValue(new DrFlitSupportCaseError(502, "No pudimos radicar tu caso.", "support_unavailable"));
    const user = renderAtConfirm();
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: "Confirmar y radicar caso" }));
    await screen.findByRole("alert");

    await user.click(screen.getByRole("button", { name: "Editar el caso" }));

    const form = screen.getByRole("form", { name: "Formulario del caso de soporte" });
    expect(within(form).getByLabelText(/^Título del caso/)).toHaveValue("Error al subir factura");
  });

  it("AC4 — adjuntos parcialmente fallidos se informan sin ocultar el número de caso", async () => {
    vi.mocked(createSupportCase).mockResolvedValue({ caseId: 13003, caseUrl: null, attachmentsFailed: 2 });
    const user = renderAtConfirm(
      applyContinueSupportCase(
        applyOpenSupportCase(
          createInitialState(),
          complete({
            adjuntar: true,
            attachments: [
              { id: "a1", filename: "uno.png", sizeBytes: 10 },
              { id: "a2", filename: "dos.png", sizeBytes: 10 },
              { id: "a3", filename: "tres.pdf", sizeBytes: 10 },
            ],
          }),
        ),
      ),
    );
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: "Confirmar y radicar caso" }));

    const created = await screen.findByRole("region", { name: "Caso de soporte radicado" });
    expect(within(created).getByText("Tu caso #13003 quedó radicado")).toBeInTheDocument();
    expect(within(created).getByText(/2 adjuntos no se pudieron incluir/)).toBeInTheDocument();
    expect(vi.mocked(createSupportCase).mock.calls[0]![0].attachmentIds).toEqual(["a1", "a2", "a3"]);
  });

  it("una radicación interrumpida por recarga no se relanza sola", () => {
    saveDrFlitSession({ open: true, state: applySubmitSupportCase(confirming()) });

    render(<DrFlitAssistant displayName="Ana" />);

    expect(createSupportCase).not.toHaveBeenCalled();
  });
});
