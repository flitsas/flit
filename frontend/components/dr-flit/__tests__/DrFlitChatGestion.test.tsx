import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { DrFlitAssistant } from "../DrFlitAssistant";
import {
  applyChatSuccess,
  applyUserText,
  createInitialState,
  isComposerEnabled,
  resetMessageIdSeq,
} from "../dr-flit-conversation";
import type { DrFlitChatResponse } from "../dr-flit-chat-types";
import { clearDrFlitSession } from "../dr-flit-session-store";

vi.mock("@/lib/api/dr-flit-client", () => ({ postDrFlitChat: vi.fn() }));
vi.mock("../dr-flit-search", async () => {
  const actual = await vi.importActual<typeof import("../dr-flit-search")>("../dr-flit-search");
  return { ...actual, searchTramites: vi.fn(), searchValidaciones: vi.fn() };
});

import { postDrFlitChat } from "@/lib/api/dr-flit-client";
import { searchTramites } from "../dr-flit-search";

/**
 * HU #12927 — intención «gestión» enrutada a la sesión Gestión existente y manejo de «no claro».
 *
 * Uso de ejemplo:
 *   applyChatSuccess(loading, { intent: "gestion", suggestGestionIntent: "placa", … }) // pide la placa
 */

function response(overrides: Partial<DrFlitChatResponse>): DrFlitChatResponse {
  return {
    status: "ok",
    intent: "gestion",
    reply: "Te llevo a la búsqueda.",
    citations: [],
    suggestGestionIntent: null,
    usage: { messagesUsedToday: 2, dailyLimit: 30 },
    ...overrides,
  };
}

const loading = () => applyUserText(createInitialState("Ana"), "búscame el trámite de la placa ABC123", { chatEnabled: true });

describe("HU #12927 — gestión y no claro (máquina de estados)", () => {
  beforeEach(() => resetMessageIdSeq());

  it("AC1 — gestión con sugerencia: pide el valor de ese tipo de búsqueda, como el menú", () => {
    const next = applyChatSuccess(loading(), response({ suggestGestionIntent: "placa" }));

    expect(next.phase).toBe("awaiting_value");
    expect(next.pendingIntent).toBe("placa");
    expect(next.session).toBe("gestion");
    expect(next.messages.slice(-2).map((m) => m.role)).toEqual(["bot", "bot"]);
    expect(next.messages.at(-2)?.text).toBe("Te llevo a la búsqueda.");
    expect(next.messages.at(-1)?.text).toMatch(/placa/i);
    // No se simula un clic del usuario en «Buscar por placa».
    expect(next.messages.some((m) => m.role === "user" && /Buscar por placa/i.test(m.text))).toBe(false);
    expect(isComposerEnabled(next)).toBe(true);
    expect(next.chatUsage).toEqual({ messagesUsedToday: 2, dailyLimit: 30 });
  });

  it.each(["vin", "tramite", "cliente"] as const)("AC1 — gestión con sugerencia %s", (target) => {
    const next = applyChatSuccess(loading(), response({ suggestGestionIntent: target }));

    expect(next.phase).toBe("awaiting_value");
    expect(next.pendingIntent).toBe(target);
  });

  it("AC2 — gestión sin sugerencia: menú de Gestión para elegir, sin error", () => {
    const next = applyChatSuccess(loading(), response({ suggestGestionIntent: null }));

    expect(next.phase).toBe("idle");
    expect(next.session).toBe("gestion");
    expect(next.showSessionMenu).toBe(true);
    expect(next.pendingIntent).toBeNull();
    expect(next.messages.at(-1)?.text).toBe("Te llevo a la búsqueda.");
  });

  it("AC3 — no claro: muestra la pregunta de seguimiento y permite seguir escribiendo al LLM", () => {
    const next = applyChatSuccess(
      loading(),
      response({ intent: "no_claro", reply: "¿Quieres buscar un trámite o saber cómo crearlo?" }),
    );

    expect(next.messages.at(-1)).toMatchObject({ role: "bot", text: "¿Quieres buscar un trámite o saber cómo crearlo?" });
    expect(next.helpResults).toBeNull();
    expect(isComposerEnabled(next)).toBe(true);
    expect(applyUserText(next, "buscarlo", { chatEnabled: true }).phase).toBe("chat_loading");
  });

  it("soporte: muestra los canales de soporte con el reply del LLM", () => {
    const next = applyChatSuccess(loading(), response({ intent: "soporte", reply: "Lamento el error, te ayudo." }));

    expect(next.phase).toBe("showing_support");
    expect(next.showSupportInfo).toBe(true);
    expect(next.messages.at(-1)?.text).toBe("Lamento el error, te ayudo.");
  });
});

describe("HU #12927 — gestión en el panel", () => {
  beforeEach(() => {
    vi.stubGlobal("open", vi.fn());
    vi.mocked(postDrFlitChat).mockReset();
    vi.mocked(searchTramites).mockReset();
    clearDrFlitSession();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    clearDrFlitSession();
  });

  it("AC1 — el LLM sugiere placa, el usuario escribe la placa y corre la búsqueda de siempre", async () => {
    vi.mocked(postDrFlitChat).mockResolvedValue(response({ suggestGestionIntent: "placa", reply: "Te llevo a buscar por placa." }));
    vi.mocked(searchTramites).mockResolvedValue({ items: [], total: 0 });
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Ana" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));

    await user.type(screen.getByRole("textbox"), "quiero ver los trámites de una placa{enter}");
    expect(await screen.findByText("Te llevo a buscar por placa.")).toBeInTheDocument();
    await user.type(screen.getByRole("textbox"), "ABC123{enter}");

    await waitFor(() => expect(searchTramites).toHaveBeenCalledWith("placa", "ABC123", expect.anything()));
    expect(postDrFlitChat).toHaveBeenCalledTimes(1);
  });

  it("AC2 — sin sugerencia aparece el menú de Gestión", async () => {
    vi.mocked(postDrFlitChat).mockResolvedValue(response({ suggestGestionIntent: null }));
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Ana" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));

    await user.type(screen.getByRole("textbox"), "necesito buscar algo{enter}");

    expect(await screen.findByText("Te llevo a la búsqueda.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Buscar por placa/i })).toBeInTheDocument();
  });
});
