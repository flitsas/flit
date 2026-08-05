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

describe("DrFlitAssistant", () => {
  beforeEach(() => {
    vi.stubGlobal("location", { ...window.location, assign: vi.fn() });
    vi.mocked(searchTramites).mockReset();
    vi.mocked(searchValidaciones).mockReset();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("muestra las 4 sugerencias", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    expect(screen.getByRole("button", { name: /Buscar por placa/i })).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /Búsqueda por Trámites/i }),
    ).toBeInTheDocument();
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
    expect(screen.getByText("Sugerencias")).toBeInTheDocument();
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
