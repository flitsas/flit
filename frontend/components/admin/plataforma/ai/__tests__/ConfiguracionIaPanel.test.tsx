import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ConfiguracionIaPanel } from "../ConfiguracionIaPanel";
import type { AiToolSettingsView } from "@/lib/api/admin-plataforma-ai";

const listAiTools = vi.fn();
const activateAiProvider = vi.fn();
const putAiToolSettings = vi.fn();
const listAiTransitions = vi.fn();
const listAiAlerts = vi.fn();
const ackAiAlert = vi.fn();

vi.mock("@/lib/api/admin-plataforma-ai", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-plataforma-ai")>(
    "@/lib/api/admin-plataforma-ai",
  );
  return {
    ...actual,
    listAiTools: (...a: unknown[]) => listAiTools(...a),
    activateAiProvider: (...a: unknown[]) => activateAiProvider(...a),
    putAiToolSettings: (...a: unknown[]) => putAiToolSettings(...a),
    listAiTransitions: (...a: unknown[]) => listAiTransitions(...a),
    listAiAlerts: (...a: unknown[]) => listAiAlerts(...a),
    ackAiAlert: (...a: unknown[]) => ackAiAlert(...a),
  };
});

function tool(over: Partial<AiToolSettingsView> = {}): AiToolSettingsView {
  return {
    toolCode: "ocr",
    activeProvider: "anthropic",
    autoReturnToPrimary: true,
    updatedAt: "2026-10-14T12:00:00Z",
    updatedBy: null,
    rowVersion: 1,
    providers: [
      {
        providerCode: "anthropic",
        hasApiKey: true,
        apiKeyMask: "••••wxyz",
        capable: true,
        enabled: true,
        priorityRank: 1,
        health: "unknown",
      },
      {
        providerCode: "openai",
        hasApiKey: true,
        apiKeyMask: "••••abcd",
        capable: true,
        enabled: true,
        priorityRank: 2,
        health: "unknown",
      },
      {
        providerCode: "kimi",
        hasApiKey: false,
        apiKeyMask: null,
        capable: true,
        enabled: true,
        priorityRank: 3,
        health: "unknown",
      },
      {
        providerCode: "deepseek",
        hasApiKey: false,
        apiKeyMask: null,
        capable: false,
        enabled: false,
        priorityRank: 4,
        health: "unknown",
      },
    ],
    ...over,
  };
}

describe("ConfiguracionIaPanel (HU #13511 / #13512)", () => {
  beforeEach(() => {
    listAiTools.mockReset();
    activateAiProvider.mockReset();
    putAiToolSettings.mockReset();
    listAiTransitions.mockReset().mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    listAiAlerts.mockReset().mockResolvedValue([]);
    ackAiAlert.mockReset();
  });

  it("HU13511 — estado loading mientras llega el GET de tools", () => {
    listAiTools.mockReturnValue(new Promise(() => {}));
    render(<ConfiguracionIaPanel />);
    expect(screen.getByTestId("ai-config-loading")).toBeInTheDocument();
    expect(screen.getByText(/Cargando configuración de IA/i)).toBeInTheDocument();
  });

  it("HU13511 — estado error con reintento", async () => {
    listAiTools.mockRejectedValueOnce(new Error("boom"));
    render(<ConfiguracionIaPanel />);
    expect(await screen.findByText(/No se pudo cargar la configuración de IA/i)).toBeInTheDocument();
    listAiTools.mockResolvedValueOnce({ tools: [tool(), tool({ toolCode: "dr_flit" })] });
    fireEvent.click(screen.getByRole("button", { name: /Reintentar/i }));
    expect(await screen.findByTestId("ai-config-ready")).toBeInTheDocument();
  });

  it("HU13511 — estado vacío cuando no hay tools", async () => {
    listAiTools.mockResolvedValueOnce({ tools: [] });
    render(<ConfiguracionIaPanel />);
    expect(await screen.findByTestId("ai-config-empty")).toBeInTheDocument();
    expect(screen.getByText(/Sin herramientas configuradas/i)).toBeInTheDocument();
  });

  it("HU13511 — estado lleno: secciones OCR y DR. FLIT con badges Configurada / No configurada", async () => {
    listAiTools.mockResolvedValueOnce({
      tools: [tool(), tool({ toolCode: "dr_flit", activeProvider: "openai" })],
    });
    render(<ConfiguracionIaPanel />);

    expect(await screen.findByTestId("ai-tool-ocr")).toBeInTheDocument();
    expect(screen.getByTestId("ai-tool-dr_flit")).toBeInTheDocument();
    expect(screen.getAllByText("Configurada").length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("No configurada").length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("••••wxyz").length).toBeGreaterThanOrEqual(1);
  });

  it("HU13512 AC1/AC4 — Activar pide confirmación; Cancelar no llama al POST", async () => {
    listAiTools.mockResolvedValueOnce({ tools: [tool()] });
    render(<ConfiguracionIaPanel />);
    await screen.findByTestId("ai-tool-ocr");

    fireEvent.click(screen.getByRole("button", { name: /Activar OpenAI en OCR/i }));
    const dialogo = await screen.findByTestId("ai-activate-confirm");
    expect(activateAiProvider).not.toHaveBeenCalled();

    fireEvent.click(within(dialogo).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByTestId("ai-activate-confirm")).not.toBeInTheDocument());
    expect(activateAiProvider).not.toHaveBeenCalled();
  });

  it("HU13512 AC1 — confirmar activación llama POST y actualiza el activo", async () => {
    listAiTools.mockResolvedValueOnce({ tools: [tool()] });
    activateAiProvider.mockResolvedValueOnce(
      tool({ activeProvider: "openai", updatedAt: "2026-10-14T13:00:00Z" }),
    );
    render(<ConfiguracionIaPanel />);
    await screen.findByTestId("ai-tool-ocr");

    fireEvent.click(screen.getByRole("button", { name: /Activar OpenAI en OCR/i }));

    const dialogo = await screen.findByTestId("ai-activate-confirm");
    fireEvent.click(within(dialogo).getByRole("button", { name: /Confirmar activación/i }));

    await waitFor(() =>
      expect(activateAiProvider).toHaveBeenCalledWith("ocr", { providerCode: "openai" }),
    );
    expect(await screen.findByText(/OpenAI quedó como proveedor activo/i)).toBeInTheDocument();
  });

  it("HU13512 AC3 — marcar alerta como leída llama ack", async () => {
    listAiTools.mockResolvedValueOnce({ tools: [tool()] });
    listAiAlerts.mockResolvedValueOnce([
      {
        id: "alerta-1",
        toolCode: "ocr",
        fromProvider: "anthropic",
        toProvider: "openai",
        occurredAt: "2026-10-14T11:00:00Z",
        acked: false,
      },
    ]);
    ackAiAlert.mockResolvedValueOnce(undefined);
    render(<ConfiguracionIaPanel />);
    await screen.findByTestId("ai-tool-ocr");

    fireEvent.click(await screen.findByRole("button", { name: /Marcar leída/i }));
    await waitFor(() => expect(ackAiAlert).toHaveBeenCalledWith("alerta-1"));
  });
});
