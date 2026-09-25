import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { DrFlitAssistant } from "../DrFlitAssistant";
import { clearDrFlitSession } from "../dr-flit-session-store";
import type { DrFlitChatResponse } from "../dr-flit-chat-types";

vi.mock("@/lib/api/dr-flit-client", () => ({
  postDrFlitChat: vi.fn(),
}));

import { postDrFlitChat } from "@/lib/api/dr-flit-client";

/**
 * HU #12926 — el panel completo con el cliente del chat sustituido.
 *
 * Uso de ejemplo:
 *   render(<DrFlitAssistant displayName="Ana" />); escribir texto libre → POST /dr-flit/chat → respuesta.
 */

const okReply: DrFlitChatResponse = {
  status: "ok",
  intent: "duda",
  reply: "Abre Trámites y pulsa «Nuevo trámite».",
  citations: [
    { slug: "1-gestor/2-crear-tramite", title: "Crear un trámite", href: "/manual/1-gestor/2-crear-tramite", sourceHref: null, primarySource: false },
  ],
  suggestGestionIntent: null,
  usage: { messagesUsedToday: 1, dailyLimit: 30 },
};

async function openAndAsk(text: string) {
  const user = userEvent.setup();
  render(<DrFlitAssistant displayName="Ana" />);
  await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
  await user.type(screen.getByRole("textbox"), `${text}{enter}`);
  return user;
}

describe("HU #12926 — chat con LLM en el panel", () => {
  beforeEach(() => {
    vi.stubGlobal("open", vi.fn());
    vi.mocked(postDrFlitChat).mockReset();
    clearDrFlitSession();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    clearDrFlitSession();
  });

  it("AC1 — muestra la respuesta del LLM y la tarjeta de la cita, que abre el manual", async () => {
    vi.mocked(postDrFlitChat).mockResolvedValue(okReply);

    const user = await openAndAsk("¿cómo creo un trámite?");

    expect(await screen.findByText("Abre Trámites y pulsa «Nuevo trámite».")).toBeInTheDocument();
    const list = screen.getByLabelText("Artículos del manual");
    await user.click(within(list).getByRole("button", { name: /Crear un trámite/i }));
    expect(window.open).toHaveBeenCalledWith("/manual/1-gestor/2-crear-tramite", "_blank", "noopener,noreferrer");
  });

  it("AC1/AC3 — envía el mensaje y el historial previo sin el mensaje en curso", async () => {
    vi.mocked(postDrFlitChat).mockResolvedValue(okReply);

    await openAndAsk("¿cómo creo un trámite?");

    await waitFor(() => expect(postDrFlitChat).toHaveBeenCalledTimes(1));
    const [request] = vi.mocked(postDrFlitChat).mock.calls[0]!;
    expect(request.message).toBe("¿cómo creo un trámite?");
    expect(request.history.map((t) => t.role)).toEqual(["assistant"]); // solo el saludo previo
    expect(request.history.some((t) => t.text === "¿cómo creo un trámite?")).toBe(false);
  });

  it("AC2 — con el LLM degradado responde el buscador local y el menú sigue disponible", async () => {
    vi.mocked(postDrFlitChat).mockResolvedValue({ ...okReply, status: "degraded", intent: "no_claro", reply: "", citations: [] });

    const user = await openAndAsk("cómo creo un trámite");

    expect(await screen.findByText(/Respuesta rápida del manual/)).toBeInTheDocument();
    expect(screen.getByLabelText("Artículos del manual")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Volver al menú" }));
    expect(screen.getByRole("button", { name: /Buscar por placa/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Soporte/i })).toBeInTheDocument();
  });

  it("AC2 — un error de red también cae al buscador local", async () => {
    vi.mocked(postDrFlitChat).mockRejectedValue(new Error("Failed to fetch"));

    await openAndAsk("cómo creo un trámite");

    expect(await screen.findByText(/Respuesta rápida del manual/)).toBeInTheDocument();
  });

  it("AC4 — la conversación sigue al cerrar y volver a abrir el panel", async () => {
    vi.mocked(postDrFlitChat).mockResolvedValue(okReply);
    const user = await openAndAsk("¿cómo creo un trámite?");
    await screen.findByText("Abre Trámites y pulsa «Nuevo trámite».");

    await user.click(screen.getByRole("button", { name: "Cerrar DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));

    expect(screen.getByText("Abre Trámites y pulsa «Nuevo trámite».")).toBeInTheDocument();
  });
});
