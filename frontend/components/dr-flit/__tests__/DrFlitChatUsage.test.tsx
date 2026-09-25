import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { DrFlitAssistant } from "../DrFlitAssistant";
import {
  applyChatRateLimited,
  applyChatSuccess,
  applyUserText,
  createInitialState,
  DR_FLIT_USAGE_WARNING_MARGIN,
  isComposerEnabled,
  remainingChatMessages,
  resetMessageIdSeq,
  shouldWarnChatUsage,
} from "../dr-flit-conversation";
import type { DrFlitChatResponse } from "../dr-flit-chat-types";
import { clearDrFlitSession } from "../dr-flit-session-store";

vi.mock("@/lib/api/dr-flit-client", () => ({ postDrFlitChat: vi.fn() }));

import { postDrFlitChat } from "@/lib/api/dr-flit-client";

/**
 * HU #12928 — aviso de tope diario y estado rate_limited.
 *
 * Uso de ejemplo:
 *   shouldWarnChatUsage(state) // true si quedan entre 1 y DR_FLIT_USAGE_WARNING_MARGIN mensajes
 */

const RATE_LIMITED_REPLY = "Llegaste al límite de mensajes con DR. FLIT por hoy.";

function ok(used: number, limit = 30): DrFlitChatResponse {
  return {
    status: "ok",
    intent: "duda",
    reply: "Así se hace.",
    citations: [],
    suggestGestionIntent: null,
    usage: { messagesUsedToday: used, dailyLimit: limit },
  };
}

const rateLimited: DrFlitChatResponse = {
  status: "rate_limited",
  intent: "no_claro",
  reply: RATE_LIMITED_REPLY,
  citations: [],
  suggestGestionIntent: null,
  usage: { messagesUsedToday: 30, dailyLimit: 30 },
};

const loading = () => applyUserText(createInitialState("Ana"), "¿cómo creo un trámite?", { chatEnabled: true });

describe("HU #12928 — tope diario (máquina de estados)", () => {
  beforeEach(() => resetMessageIdSeq());

  it.each([
    [1, false],
    [30 - DR_FLIT_USAGE_WARNING_MARGIN - 1, false],
    [30 - DR_FLIT_USAGE_WARNING_MARGIN, true],
    [29, true],
    [30, false],
  ])("AC1 — con %i de 30 usados, avisar = %s", (used, warn) => {
    const next = applyChatSuccess(loading(), ok(used));

    expect(shouldWarnChatUsage(next)).toBe(warn);
    expect(remainingChatMessages(next)).toBe(30 - used);
  });

  it("AC1 — sin dato de uso (conversación anterior a la épica) no avisa", () => {
    expect(shouldWarnChatUsage(createInitialState())).toBe(false);
    expect(remainingChatMessages(createInitialState())).toBeNull();
  });

  it("AC2 — tope alcanzado: mensaje del backend y vuelta al menú", () => {
    const next = applyChatRateLimited(loading(), rateLimited);

    expect(next.phase).toBe("idle");
    expect(next.showSessionMenu).toBe(true);
    expect(next.isTyping).toBe(false);
    expect(next.messages.at(-1)).toMatchObject({ role: "bot", text: RATE_LIMITED_REPLY });
    expect(next.chatUsage).toEqual({ messagesUsedToday: 30, dailyLimit: 30 });
    expect(shouldWarnChatUsage(next)).toBe(false);
  });

  it("AC3 — tras el tope el frontend no bloquea: el siguiente mensaje va al backend", () => {
    const limited = applyChatRateLimited(loading(), rateLimited);

    expect(isComposerEnabled(limited)).toBe(true);
    const nextDay = applyUserText(limited, "hola de nuevo", { chatEnabled: true });
    expect(nextDay.phase).toBe("chat_loading");

    // El backend ya reinició el conteo: el uso nuevo reemplaza al anterior.
    const answered = applyChatSuccess(nextDay, ok(1));
    expect(answered.chatUsage).toEqual({ messagesUsedToday: 1, dailyLimit: 30 });
    expect(shouldWarnChatUsage(answered)).toBe(false);
  });
});

describe("HU #12928 — tope diario en el panel", () => {
  beforeEach(() => {
    vi.stubGlobal("open", vi.fn());
    vi.mocked(postDrFlitChat).mockReset();
    clearDrFlitSession();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    clearDrFlitSession();
  });

  async function ask(text: string) {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Ana" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.type(screen.getByRole("textbox"), `${text}{enter}`);
    return user;
  }

  it("AC1 — cerca del tope muestra un aviso discreto con el conteo sin cortar la respuesta", async () => {
    vi.mocked(postDrFlitChat).mockResolvedValue(ok(27));

    await ask("¿cómo creo un trámite?");

    expect(await screen.findByText("Así se hace.")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("Te quedan 3 mensajes con el asistente hoy (27 de 30)");
  });

  it("AC1 — lejos del tope no hay aviso", async () => {
    vi.mocked(postDrFlitChat).mockResolvedValue(ok(3));

    await ask("¿cómo creo un trámite?");

    await screen.findByText("Así se hace.");
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("AC2 — con el tope alcanzado muestra el mensaje y el menú completo sigue funcionando", async () => {
    vi.mocked(postDrFlitChat).mockResolvedValue(rateLimited);

    const user = await ask("¿cómo creo un trámite?");

    expect(await screen.findByText(RATE_LIMITED_REPLY)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Normativa/i }));
    expect(screen.getByLabelText("Artículos del manual")).toBeInTheDocument();
  });

  it("AC3 — al día siguiente vuelve a responder con el LLM sin acción del usuario", async () => {
    vi.mocked(postDrFlitChat).mockResolvedValueOnce(rateLimited).mockResolvedValueOnce(ok(1));

    const user = await ask("primera");
    await screen.findByText(RATE_LIMITED_REPLY);
    await user.type(screen.getByRole("textbox"), "al otro día{enter}");

    expect(await screen.findByText("Así se hace.")).toBeInTheDocument();
    expect(postDrFlitChat).toHaveBeenCalledTimes(2);
  });
});
