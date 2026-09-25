import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { DrFlitAssistant } from "../DrFlitAssistant";
import {
  applyDismissPrivacyNotice,
  createInitialState,
  shouldShowPrivacyNotice,
} from "../dr-flit-conversation";
import type { DrFlitChatResponse } from "../dr-flit-chat-types";
import { clearDrFlitSession, loadDrFlitSession } from "../dr-flit-session-store";

vi.mock("@/lib/api/dr-flit-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/dr-flit-client")>("@/lib/api/dr-flit-client");
  return { ...actual, postDrFlitChat: vi.fn() };
});

import { postDrFlitChat } from "@/lib/api/dr-flit-client";

/**
 * HU #12931 — aviso de Habeas Data al abrir DR. FLIT: no bloqueante y sin repetirse en la sesión.
 *
 * Uso de ejemplo:
 *   shouldShowPrivacyNotice(state) // true hasta «Entendido» o el primer mensaje
 */

const ok: DrFlitChatResponse = {
  status: "ok",
  intent: "duda",
  reply: "Así se hace.",
  citations: [],
  suggestGestionIntent: null,
  usage: { messagesUsedToday: 1, dailyLimit: 30 },
};

describe("HU #12931 — aviso de tratamiento de datos", () => {
  beforeEach(() => {
    vi.stubGlobal("open", vi.fn());
    vi.mocked(postDrFlitChat).mockReset().mockResolvedValue(ok);
    clearDrFlitSession();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    clearDrFlitSession();
  });

  it("estado: se muestra en una conversación nueva y deja de mostrarse al reconocerlo", () => {
    const initial = createInitialState("Ana");

    expect(shouldShowPrivacyNotice(initial)).toBe(true);
    const dismissed = applyDismissPrivacyNotice(initial);
    expect(shouldShowPrivacyNotice(dismissed)).toBe(false);
    expect(applyDismissPrivacyNotice(dismissed)).toBe(dismissed);
  });

  it("AC1 — al abrir el panel se muestra el aviso sin bloquear el chat", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Ana" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));

    const notice = screen.getByRole("region", { name: "Tratamiento de datos" });
    expect(notice).toHaveTextContent("inteligencia artificial");
    expect(notice).toHaveTextContent("Ley 1581 de 2012");
    expect(screen.getByRole("textbox")).toBeEnabled();
    expect(screen.getByRole("button", { name: /Buscar por placa/i })).toBeEnabled();
  });

  it("AC2 — sin tocar el aviso, el primer mensaje funciona y el aviso se retira", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Ana" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));

    await user.type(screen.getByRole("textbox"), "¿cómo creo un trámite?{enter}");

    expect(await screen.findByText("Así se hace.")).toBeInTheDocument();
    expect(postDrFlitChat).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("region", { name: "Tratamiento de datos" })).not.toBeInTheDocument();
  });

  it("AC2 — «Entendido» retira el aviso", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Ana" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));

    await user.click(screen.getByRole("button", { name: "Entendido" }));

    expect(screen.queryByRole("region", { name: "Tratamiento de datos" })).not.toBeInTheDocument();
  });

  it("AC3 — visto una vez, no se repite al cerrar y volver a abrir en la misma sesión", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Ana" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: "Entendido" }));

    await user.click(screen.getByRole("button", { name: "Cerrar DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));

    expect(screen.queryByRole("region", { name: "Tratamiento de datos" })).not.toBeInTheDocument();
    expect(loadDrFlitSession()?.state.privacyNoticeSeen).toBe(true);
  });
});

describe("Prueba integrada 2026-09-25 — hallazgos corregidos", () => {
  beforeEach(() => {
    vi.mocked(postDrFlitChat).mockReset();
    clearDrFlitSession();
  });

  afterEach(() => clearDrFlitSession());

  it("el panel recorta con overflow-clip: un focus() interno no puede desplazarlo", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Ana" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));

    const panel = screen.getByRole("dialog");
    expect(panel.className).toContain("overflow-clip");
    expect(panel.className).not.toContain("overflow-hidden");
  });

  it("sin nombre en el perfil, el formulario del caso no usa el correo como nombre", async () => {
    vi.mocked(postDrFlitChat).mockResolvedValue({
      status: "ok",
      intent: "soporte",
      reply: "Te ayudo.",
      citations: [],
      suggestGestionIntent: null,
      usage: { messagesUsedToday: 1, dailyLimit: 30 },
    });
    const user = userEvent.setup();
    render(
      <DrFlitAssistant
        displayName="demo@flit.local"
        supportContact={{ name: null, email: "demo@flit.local" }}
      />,
    );
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.type(screen.getByRole("textbox"), "tengo un error{enter}");

    const form = await screen.findByRole("form", { name: "Formulario del caso de soporte" });
    expect(within(form).getByLabelText(/^Nombre/)).toHaveValue("");
    expect(within(form).getByLabelText(/^Correo/)).toHaveValue("demo@flit.local");
  });
});
