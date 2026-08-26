import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";

import { render, screen, waitFor } from "@testing-library/react";

import userEvent from "@testing-library/user-event";

import { DrFlitAssistant } from "../DrFlitAssistant";

vi.mock("../dr-flit-search", async () => {
  const actual = await vi.importActual<typeof import("../dr-flit-search")>(
    "../dr-flit-search",
  );
  return {
    ...actual,
    searchTramites: vi.fn(),
    searchValidaciones: vi.fn(),
  };
});

import { searchTramites, searchValidaciones } from "../dr-flit-search";

import { DR_FLIT_SUPPORT_CASE_URL } from "../dr-flit-intents";

describe("DrFlitAssistant", () => {
  beforeEach(() => {
    vi.stubGlobal("open", vi.fn());
    vi.mocked(searchTramites).mockReset();
    vi.mocked(searchValidaciones).mockReset();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("muestra sesiones Gestión y Ayuda en el chat", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));

    expect(screen.queryByRole("tablist")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Gestión")).toBeInTheDocument();
    expect(screen.getByLabelText("Ayuda")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Buscar por placa/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Necesito ayuda/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Soporte/i })).toBeInTheDocument();
  });

  it("Soporte muestra canales y abre formulario oficial", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Soporte/i }));

    expect(screen.getByText("soporte@flitsas.com")).toBeInTheDocument();
    expect(screen.getByText("300 000 0000")).toBeInTheDocument();

    await user.click(
      screen.getByRole("button", { name: /Generar un caso de soporte/i }),
    );
    expect(window.open).toHaveBeenCalledWith(
      DR_FLIT_SUPPORT_CASE_URL,
      "_blank",
      "noopener,noreferrer",
    );
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("placa consulta API y muestra resultados", async () => {
    vi.mocked(searchTramites).mockResolvedValue([
      {
        id: "11111111-1111-4111-a111-111111111111",
        fecha: "2026-03-01",
        estado: "borrador",
        placa: "ABC123",
        vin: "VIN1",
        tipoTramite: "Traspaso",
        href: "/tramites/11111111-1111-4111-a111-111111111111",
      },
    ]);

    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Buscar por placa/i }));
    await user.type(
      screen.getByPlaceholderText("Pregúntale a DR. FLIT..."),
      "ABC123{Enter}",
    );

    await waitFor(() => {
      expect(searchTramites).toHaveBeenCalledWith(
        "placa",
        "ABC123",
        expect.any(Object),
      );
    });
    await waitFor(() => {
      expect(screen.getByLabelText("Resultados de trámites")).toBeInTheDocument();
    });
    expect(screen.getByRole("button", { name: /Ver trámite/i })).toBeInTheDocument();
  });

  it("al reabrir reinicia la conversación", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Buscar por placa/i }));
    expect(
      screen.getByText("Indícame el valor de placa a consultar."),
    ).toBeInTheDocument();

    await user.keyboard("{Escape}");
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    expect(screen.getByLabelText("Sesiones del chat")).toBeInTheDocument();
    expect(
      screen.queryByText("Indícame el valor de placa a consultar."),
    ).not.toBeInTheDocument();
  });

  it("cierra con Escape", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
