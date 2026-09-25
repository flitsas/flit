import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { ApiError } from "@/lib/api/types";
import { DrFlitAssistant } from "../DrFlitAssistant";
import {
  applyConsentAccepted,
  applyConsentDeclined,
  applyOpenSupportCase,
  applyContinueSupportCase,
  applyUserText,
  createInitialState,
  hasConsent,
  resetMessageIdSeq,
} from "../dr-flit-conversation";
import type { DrFlitChatResponse } from "../dr-flit-chat-types";
import { clearDrFlitSession } from "../dr-flit-session-store";
import { createSupportDraft } from "../dr-flit-support-case";

vi.mock("@/lib/api/dr-flit-client", async () => ({
  ...(await vi.importActual<typeof import("@/lib/api/dr-flit-client")>("@/lib/api/dr-flit-client")),
  postDrFlitChat: vi.fn(),
  getDrFlitConsent: vi.fn(),
  acceptDrFlitConsent: vi.fn(),
  createSupportCase: vi.fn(),
}));

import {
  acceptDrFlitConsent,
  createSupportCase,
  getDrFlitConsent,
  postDrFlitChat,
} from "@/lib/api/dr-flit-client";

/**
 * HU #12931 — consentimiento obligatorio del tratamiento de datos para el chat con IA y el caso de
 * soporte. El menú sin IA no lo necesita; «Ahora no» no reinicia nada; se acepta una vez por versión.
 *
 * Uso de ejemplo:
 *   applyUserText(state, "hola", { chatEnabled: true, requireConsent: true }) // awaiting_consent
 *   applyConsentAccepted(state, { version, accepted: true }, draft)           // continúa lo pendiente
 */

const VERSION = "2026-09-25";
const reply: DrFlitChatResponse = {
  status: "ok",
  intent: "duda",
  reply: "Así se crea un trámite.",
  citations: [],
  suggestGestionIntent: null,
  usage: { messagesUsedToday: 1, dailyLimit: 30 },
};
const draft = () => createSupportDraft({ name: "Ana", email: "ana@example.test", company: "Empresa" }, null);

describe("HU #12931 — máquina de estados", () => {
  beforeEach(() => resetMessageIdSeq());

  it("AC1 — sin consentimiento el texto libre queda esperando la autorización, sin ir al LLM", () => {
    const next = applyUserText(createInitialState(), "¿cómo creo un trámite?", { chatEnabled: true, requireConsent: true });

    expect(next.phase).toBe("awaiting_consent");
    expect(next.pendingConsent).toEqual({ kind: "chat", text: "¿cómo creo un trámite?" });
    expect(next.messages.at(-1)).toMatchObject({ role: "user", text: "¿cómo creo un trámite?" });
    expect(next.isTyping).toBe(false);
  });

  it("AC1 — al aceptar continúa lo pendiente: chat, formulario o radicación", () => {
    const accepted = { version: VERSION, accepted: true };
    const chat = applyUserText(createInitialState(), "hola", { chatEnabled: true, requireConsent: true });
    expect(applyConsentAccepted(chat, accepted, draft()).phase).toBe("chat_loading");
    expect(applyConsentAccepted(chat, accepted, draft()).queryValue).toBe("hola");

    const support = { ...createInitialState(), phase: "awaiting_consent" as const, pendingConsent: { kind: "support" as const } };
    const opened = applyConsentAccepted(support, accepted, draft());
    expect(opened.phase).toBe("collecting_support_case");
    expect(hasConsent(opened)).toBe(true);

    const confirming = applyContinueSupportCase(
      applyOpenSupportCase(createInitialState(), {
        ...draft(), titulo: "T", detalle: "D", resultadoEsperado: "R", frecuencia: "siempre", prioridad: "Alta",
      }),
    );
    const submit = applyConsentAccepted({ ...confirming, phase: "awaiting_consent", pendingConsent: { kind: "submit" } }, accepted, draft());
    expect(submit.phase).toBe("submitting_support_case");
  });

  it("AC2 — «Ahora no» vuelve al menú con un mensaje, sin nada pendiente", () => {
    const waiting = applyUserText(createInitialState(), "hola", { chatEnabled: true, requireConsent: true });

    const next = applyConsentDeclined(waiting);

    expect(next.phase).toBe("idle");
    expect(next.showSessionMenu).toBe(true);
    expect(next.pendingConsent).toBeNull();
    expect(next.messages.at(-1)?.text).toMatch(/Sin tu autorización no uso el asistente con IA/);
  });

  it("con consentimiento el texto libre va directo al LLM", () => {
    const next = applyUserText(createInitialState(), "hola", { chatEnabled: true, requireConsent: false });

    expect(next.phase).toBe("chat_loading");
  });
});

describe("HU #12931 — en el panel", () => {
  beforeEach(() => {
    vi.stubGlobal("open", vi.fn());
    vi.mocked(postDrFlitChat).mockReset().mockResolvedValue(reply);
    vi.mocked(getDrFlitConsent).mockReset().mockResolvedValue({ version: VERSION, accepted: false });
    vi.mocked(acceptDrFlitConsent).mockReset().mockResolvedValue({ version: VERSION, accepted: true });
    vi.mocked(createSupportCase).mockReset();
    clearDrFlitSession();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    clearDrFlitSession();
  });

  async function openPanel() {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Ana" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await waitFor(() => expect(getDrFlitConsent).toHaveBeenCalled());
    return user;
  }

  it("AC1 — pide la autorización antes del LLM y, al aceptar, registra la versión y envía el mensaje", async () => {
    const user = await openPanel();

    await user.type(screen.getByRole("textbox"), "¿cómo creo un trámite?{enter}");

    const prompt = screen.getByRole("region", { name: "Autorización de tratamiento de datos" });
    expect(prompt).toHaveTextContent("Ley 1581 de 2012");
    expect(postDrFlitChat).not.toHaveBeenCalled();

    await user.click(within(prompt).getByRole("button", { name: "Acepto" }));

    expect(acceptDrFlitConsent).toHaveBeenCalledWith(VERSION);
    expect(await screen.findByText("Así se crea un trámite.")).toBeInTheDocument();
    expect(vi.mocked(postDrFlitChat).mock.calls[0]![0].message).toBe("¿cómo creo un trámite?");
  });

  it("AC1 — el formulario del caso también exige la autorización", async () => {
    const user = await openPanel();
    await user.click(screen.getByRole("button", { name: /Soporte/i }));
    await user.click(screen.getByRole("button", { name: "Generar un caso de soporte" }));

    expect(screen.queryByRole("form", { name: "Formulario del caso de soporte" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Acepto" }));

    expect(await screen.findByRole("form", { name: "Formulario del caso de soporte" })).toBeInTheDocument();
  });

  it("AC2 — «Ahora no» no envía nada, el menú sin IA funciona y al escribir de nuevo se vuelve a pedir", async () => {
    const user = await openPanel();
    await user.type(screen.getByRole("textbox"), "hola{enter}");

    await user.click(screen.getByRole("button", { name: "Ahora no" }));

    expect(postDrFlitChat).not.toHaveBeenCalled();
    expect(acceptDrFlitConsent).not.toHaveBeenCalled();
    expect(screen.getByText(/Sin tu autorización no uso el asistente con IA/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Normativa/i }));
    expect(screen.getByLabelText("Artículos del manual")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Volver al menú" }));
    await user.type(screen.getByRole("textbox"), "otra pregunta{enter}");
    expect(screen.getByRole("region", { name: "Autorización de tratamiento de datos" })).toBeInTheDocument();
  });

  it("AC2 — la búsqueda de Gestión no pide autorización", async () => {
    const user = await openPanel();

    await user.click(screen.getByRole("button", { name: /Buscar por placa/i }));

    expect(screen.queryByRole("region", { name: "Autorización de tratamiento de datos" })).not.toBeInTheDocument();
    expect(screen.getByRole("textbox")).toBeEnabled();
  });

  it("AC3 — si ya aceptó la versión vigente, no se le vuelve a pedir", async () => {
    vi.mocked(getDrFlitConsent).mockResolvedValue({ version: VERSION, accepted: true });
    const user = await openPanel();
    await waitFor(() => expect(getDrFlitConsent).toHaveBeenCalled());

    await user.type(screen.getByRole("textbox"), "¿cómo creo un trámite?{enter}");

    expect(await screen.findByText("Así se crea un trámite.")).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Autorización de tratamiento de datos" })).not.toBeInTheDocument();
  });

  it("AC3 — si el backend responde 428 (cambió la versión), se pide de nuevo y el mensaje se reenvía al aceptar", async () => {
    vi.mocked(getDrFlitConsent).mockResolvedValue({ version: VERSION, accepted: true });
    vi.mocked(postDrFlitChat)
      .mockRejectedValueOnce(new ApiError(428, "Falta aceptar", { code: "consent_required", version: "2026-10-01" }))
      .mockResolvedValueOnce(reply);
    vi.mocked(acceptDrFlitConsent).mockResolvedValue({ version: "2026-10-01", accepted: true });
    const user = await openPanel();

    await user.type(screen.getByRole("textbox"), "hola{enter}");
    await user.click(await screen.findByRole("button", { name: "Acepto" }));

    expect(acceptDrFlitConsent).toHaveBeenCalledWith("2026-10-01");
    expect(await screen.findByText("Así se crea un trámite.")).toBeInTheDocument();
    expect(postDrFlitChat).toHaveBeenCalledTimes(2);
  });

  it("si el texto cambió mientras lo leía (409), pide leerlo de nuevo sin continuar", async () => {
    vi.mocked(acceptDrFlitConsent).mockRejectedValue(new ApiError(409, "Versión desactualizada", { code: "consent_version_mismatch", version: "2026-10-01" }));
    const user = await openPanel();
    await user.type(screen.getByRole("textbox"), "hola{enter}");

    await user.click(screen.getByRole("button", { name: "Acepto" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("El texto se actualizó");
    expect(postDrFlitChat).not.toHaveBeenCalled();
  });
});
