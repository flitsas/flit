import { afterEach, beforeEach, describe, expect, it } from "vitest";

import {
  applyChatDegraded,
  applyChatSend,
  applyChatSuccess,
  applyUserText,
  buildChatHistory,
  citationToHelpResult,
  createInitialState,
  DR_FLIT_CHAT_HISTORY_LIMIT,
  isComposerEnabled,
  resetMessageIdSeq,
  type DrFlitChatState,
} from "../dr-flit-conversation";
import type { DrFlitChatResponse } from "../dr-flit-chat-types";
import {
  clearDrFlitSession,
  loadDrFlitSession,
  saveDrFlitSession,
} from "../dr-flit-session-store";

/**
 * HU #12926 — texto libre al chat con LLM, respuesta con citas, degradado al buscador local, historial
 * acotado y persistencia en sessionStorage.
 *
 * Uso de ejemplo:
 *   const loading = applyUserText(state, "¿cómo creo un trámite?", { chatEnabled: true }); // chat_loading
 *   const next = applyChatSuccess(loading, response);                                     // showing_chat_reply
 */

function okResponse(overrides: Partial<DrFlitChatResponse> = {}): DrFlitChatResponse {
  return {
    status: "ok",
    intent: "duda",
    reply: "Abre **Trámites** y pulsa «Nuevo trámite».",
    citations: [
      { slug: "1-gestor/2-crear-tramite", title: "Crear un trámite", href: "/manual/1-gestor/2-crear-tramite", sourceHref: null, primarySource: false },
    ],
    suggestGestionIntent: null,
    usage: { messagesUsedToday: 3, dailyLimit: 30 },
    ...overrides,
  };
}

function sent(text = "¿cómo creo un trámite?"): DrFlitChatState {
  return applyUserText(createInitialState("Ana"), text, { chatEnabled: true });
}

describe("HU #12926 — chat con LLM", () => {
  beforeEach(() => {
    resetMessageIdSeq();
    clearDrFlitSession();
  });

  afterEach(() => clearDrFlitSession());

  // ── AC1 — duda con cita ────────────────────────────────────────────────────────────

  it("AC1 — el texto libre en idle entra a chat_loading con el mensaje del usuario", () => {
    const next = sent();

    expect(next.phase).toBe("chat_loading");
    expect(next.isTyping).toBe(true);
    expect(next.queryValue).toBe("¿cómo creo un trámite?");
    expect(next.messages.at(-1)).toMatchObject({ role: "user", text: "¿cómo creo un trámite?" });
    expect(next.showSessionMenu).toBe(false);
  });

  it("AC1 — respuesta ok de tipo duda: muestra el reply y tarjetas de cita con enlace al manual", () => {
    const next = applyChatSuccess(sent(), okResponse());

    expect(next.phase).toBe("showing_chat_reply");
    expect(next.isTyping).toBe(false);
    expect(next.messages.at(-1)).toMatchObject({ role: "bot", text: "Abre **Trámites** y pulsa «Nuevo trámite»." });
    expect(next.helpResults).toHaveLength(1);
    expect(next.helpResults?.[0]).toMatchObject({ slug: "1-gestor/2-crear-tramite", href: "/manual/1-gestor/2-crear-tramite" });
    // La tarjeta toma resumen y audiencia del catálogo local.
    expect(next.helpResults?.[0]?.summary.length).toBeGreaterThan(0);
    expect(next.chatUsage).toEqual({ messagesUsedToday: 3, dailyLimit: 30 });
    expect(next.showBackToSearch).toBe(true);
  });

  it("AC1 — se puede seguir escribiendo tras la respuesta del LLM", () => {
    const reply = applyChatSuccess(sent(), okResponse());

    expect(isComposerEnabled(reply)).toBe(true);
    const again = applyUserText(reply, "¿y para traspaso?", { chatEnabled: true });
    expect(again.phase).toBe("chat_loading");
  });

  it("AC1 — duda sin citas no muestra tarjetas", () => {
    const next = applyChatSuccess(sent(), okResponse({ citations: [], reply: "No encuentro eso en la documentación." }));

    expect(next.helpResults).toBeNull();
    expect(next.phase).toBe("showing_chat_reply");
  });

  it("AC1 — una cita de un slug que el frontend no conoce se muestra con los datos del backend", () => {
    const card = citationToHelpResult({
      slug: "9-nuevo/articulo",
      title: "Artículo nuevo",
      href: "/manual/9-nuevo/articulo",
      sourceHref: "/legal/norma.pdf",
      primarySource: true,
    });

    expect(card).toMatchObject({
      slug: "9-nuevo/articulo",
      title: "Artículo nuevo",
      href: "/manual/9-nuevo/articulo",
      sourceHref: "/legal/norma.pdf",
      primarySource: true,
    });
  });

  it("sin el flag del chat conserva el comportamiento previo (pedir una opción del menú)", () => {
    const next = applyUserText(createInitialState("Ana"), "hola", { chatEnabled: false });

    expect(next.phase).toBe("idle");
    expect(next.messages.at(-1)?.text).toBe("Elige una opción de Gestión o Ayuda.");
  });

  it("«Necesito ayuda» con el chat activo: la pregunta va al LLM; con el chat apagado, al buscador local", () => {
    // Cambio de usabilidad (HU #12931): un solo camino para preguntar. Antes «Necesito ayuda» usaba
    // siempre el buscador local y el usuario no veía la diferencia con escribir en la caja.
    const state: DrFlitChatState = { ...createInitialState("Ana"), phase: "awaiting_help_query", showSessionMenu: false };

    expect(applyUserText(state, "cómo creo un trámite", { chatEnabled: true }).phase).toBe("chat_loading");
    expect(applyUserText(state, "cómo creo un trámite", { chatEnabled: false }).phase).toBe("showing_help");
  });

  // ── AC2 — LLM caído ────────────────────────────────────────────────────────────────

  it("AC2 — degradado: responde el buscador local con nota de respuesta rápida", () => {
    const next = applyChatDegraded(sent("cómo creo un trámite"), {}, { messagesUsedToday: 4, dailyLimit: 30 });

    expect(next.phase).toBe("showing_help");
    expect(next.isTyping).toBe(false);
    expect(next.helpResults?.length).toBeGreaterThan(0);
    expect(next.messages.at(-1)?.text).toMatch(/Respuesta rápida del manual/);
    expect(next.showBackToSearch).toBe(true);
    expect(next.chatUsage).toEqual({ messagesUsedToday: 4, dailyLimit: 30 });
  });

  it("AC2 — degradado sin coincidencias: ofrece el Centro de Ayuda", () => {
    const next = applyChatDegraded(sent("xyzzy qwerty"));

    expect(next.helpResults).toEqual([]);
    expect(next.manualHomeHref).toBe("/manual");
    expect(next.messages.at(-1)?.text).toMatch(/no encontré un artículo del manual/);
  });

  // ── AC3 — historial acotado ────────────────────────────────────────────────────────

  it("AC3 — el historial excluye el mensaje en curso y se acota a los últimos 12 turnos", () => {
    let state = createInitialState("Ana");
    for (let i = 0; i < 10; i++) {
      state = applyChatSuccess(applyUserText(state, `pregunta ${i}`, { chatEnabled: true }), okResponse({ reply: `respuesta ${i}`, citations: [] }));
    }
    const loading = applyUserText(state, "pregunta final", { chatEnabled: true });

    const history = buildChatHistory(loading);

    expect(history).toHaveLength(DR_FLIT_CHAT_HISTORY_LIMIT);
    expect(history.at(-1)).toEqual({ role: "assistant", text: "respuesta 9" });
    expect(history.some((t) => t.text === "pregunta final")).toBe(false);
    expect(history.every((t) => t.role === "user" || t.role === "assistant")).toBe(true);
  });

  it("AC3 — cada turno se recorta al máximo del contrato", () => {
    const long = "a".repeat(2500);
    const state = applyChatSend({ ...createInitialState(), messages: [
      { id: "m1", role: "user", text: long },
      { id: "m2", role: "bot", text: "ok" },
      { id: "m3", role: "user", text: "siguiente" },
    ] }, "siguiente");

    const history = buildChatHistory(state);

    expect(history[0]?.text).toHaveLength(2000);
    expect(history).toHaveLength(2);
  });

  // ── AC4 — persistencia de sesión ───────────────────────────────────────────────────

  it("AC4 — la conversación con el LLM se conserva en sessionStorage hasta Terminar chat", () => {
    const reply = applyChatSuccess(sent(), okResponse());

    saveDrFlitSession({ open: false, state: reply });
    const restored = loadDrFlitSession();

    expect(restored?.state.phase).toBe("showing_chat_reply");
    expect(restored?.state.messages.map((m) => m.text)).toEqual(reply.messages.map((m) => m.text));
    expect(restored?.state.chatUsage).toEqual({ messagesUsedToday: 3, dailyLimit: 30 });

    clearDrFlitSession();
    expect(loadDrFlitSession()).toBeNull();
  });
});
