import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";

import { render, screen, waitFor, within } from "@testing-library/react";

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
import { clearDrFlitSession } from "../dr-flit-session-store";

describe("DrFlitAssistant", () => {
  beforeEach(() => {
    vi.stubGlobal("open", vi.fn());
    vi.mocked(searchTramites).mockReset();
    vi.mocked(searchValidaciones).mockReset();
    clearDrFlitSession();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    clearDrFlitSession();
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
    expect(screen.getByRole("button", { name: /Normativa/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Soporte/i })).toBeInTheDocument();
  });

  it("Normativa — abre el resumen del manual o el PDF de la resolución en pestaña nueva", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Normativa/i }));

    // Aparece en el mensaje del bot y en el resumen de la tarjeta.
    expect(screen.getAllByText(/fuente principal que respalda a FLIT/).length).toBeGreaterThanOrEqual(2);
    const list = screen.getByLabelText("Artículos del manual");
    await user.click(within(list).getByRole("button", { name: /Abrir la norma \(PDF\)/ }));
    expect(window.open).toHaveBeenCalledWith(
      "/legal/resolucion-20233040017145-2023-mintransporte.pdf",
      "_blank",
      "noopener,noreferrer",
    );
    await user.click(within(list).getByRole("button", { name: /Resolución 20233040017145 de 2023/ }));
    expect(window.open).toHaveBeenCalledWith(
      "/manual/5-normativa/1-resolucion-20233040017145-2023",
      "_blank",
      "noopener,noreferrer",
    );
    // Sigue habiendo salida al menú: la opción no es un callejón sin salida.
    expect(screen.getByRole("button", { name: "Volver al menú" })).toBeInTheDocument();
  });

  it("Soporte muestra canales y abre formulario oficial", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Soporte/i }));

    expect(screen.getByText("soporte@flitsas.com")).toBeInTheDocument();
    // HU-F — sin NEXT_PUBLIC_DR_FLIT_SUPPORT_PHONE no se inventa una línea de atención.
    expect(screen.queryByText("Línea de atención")).not.toBeInTheDocument();

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
    vi.mocked(searchTramites).mockResolvedValue({
      items: [
        {
          id: "11111111-1111-4111-a111-111111111111",
          radicado: "R-2026-001",
          fecha: "01/03/2026 10:00",
          estado: "borrador",
          placa: "ABC123",
          vin: "VIN1",
          tipoTramite: "Traspaso",
          compania: null,
          href: "/tramites/11111111-1111-4111-a111-111111111111",
        },
      ],
      total: 1,
    });

    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Buscar por placa/i }));
    await user.type(
      screen.getByPlaceholderText("Pregúntale a DR. FLIT..."),
      "ABC123{Enter}",
    );

    await waitFor(() => {
      // Sin JWT en el test: rol efectivo gestor, sin alcance de red.
      expect(searchTramites).toHaveBeenCalledWith(
        "placa",
        "ABC123",
        expect.objectContaining({ role: "gestor", network: { active: false } }),
      );
    });
    await waitFor(() => {
      expect(screen.getByLabelText("Resultados de trámites")).toBeInTheDocument();
    });
    expect(screen.getByText("Radicado R-2026-001")).toBeInTheDocument();
    // HU-E — pista de acción por estado (borrador) y etiquetas del catálogo canónico.
    expect(screen.getByText(/Aún no radicado/)).toBeInTheDocument();
    expect(screen.getByText("Fecha radicación")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Ver trámite/i })).toBeInTheDocument();

    // HU-C — atajo al historial de la placa consultada.
    await user.click(
      screen.getByRole("button", { name: /Ver historial completo de la placa/i }),
    );
    expect(window.open).toHaveBeenCalledWith(
      "/?m=historial-placa&placa=ABC123",
      "_blank",
      "noopener,noreferrer",
    );
  });

  it("HU-C — sin el módulo Historial por placa no se ofrece el atajo", async () => {
    vi.mocked(searchTramites).mockResolvedValue({
      items: [
        {
          id: "11111111-1111-4111-a111-111111111111",
          radicado: "R-2026-001",
          fecha: "01/03/2026 10:00",
          estado: "borrador",
          placa: "ABC123",
          vin: "VIN1",
          tipoTramite: "Traspaso",
          compania: null,
          href: "/tramites/11111111-1111-4111-a111-111111111111",
        },
      ],
      total: 1,
    });

    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" historialPlacaEnabled={false} />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Buscar por placa/i }));
    await user.type(
      screen.getByPlaceholderText("Pregúntale a DR. FLIT..."),
      "ABC123{Enter}",
    );

    await waitFor(() => {
      expect(screen.getByLabelText("Resultados de trámites")).toBeInTheDocument();
    });
    expect(
      screen.queryByRole("button", { name: /Ver historial completo de la placa/i }),
    ).not.toBeInTheDocument();
  });

  it("al cerrar con X conserva la conversación al reabrir", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Buscar por placa/i }));
    expect(
      screen.getByText("Indícame el valor de placa a consultar."),
    ).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Cerrar DR. FLIT" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    expect(
      screen.getByText("Indícame el valor de placa a consultar."),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", {
        name: "Terminar chat y borrar la conversación",
      }),
    ).toBeInTheDocument();
  });

  it("Terminar chat reinicia la conversación sin cerrar el panel", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Buscar por placa/i }));
    expect(
      screen.getByText("Indícame el valor de placa a consultar."),
    ).toBeInTheDocument();

    await user.click(
      screen.getByRole("button", {
        name: "Terminar chat y borrar la conversación",
      }),
    );
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByLabelText("Sesiones del chat")).toBeInTheDocument();
    expect(
      screen.queryByText("Indícame el valor de placa a consultar."),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", {
        name: "Terminar chat y borrar la conversación",
      }),
    ).not.toBeInTheDocument();
  });

  it("persiste conversación tras remount pero cierra el panel", async () => {
    const user = userEvent.setup();
    const { unmount } = render(<DrFlitAssistant displayName="Juan" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Buscar por placa/i }));
    expect(
      screen.getByText("Indícame el valor de placa a consultar."),
    ).toBeInTheDocument();

    unmount();
    render(<DrFlitAssistant displayName="Juan" />);

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    expect(
      screen.getByText("Indícame el valor de placa a consultar."),
    ).toBeInTheDocument();
  });

  it("al cambiar routeScope cierra el panel y conserva la conversación", async () => {
    const user = userEvent.setup();
    const { rerender } = render(
      <DrFlitAssistant displayName="Juan" routeScope="/|dashboard" />,
    );
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Buscar por placa/i }));
    expect(screen.getByRole("dialog")).toBeInTheDocument();

    rerender(
      <DrFlitAssistant displayName="Juan" routeScope="/tramites|tramites" />,
    );

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    expect(
      screen.getByText("Indícame el valor de placa a consultar."),
    ).toBeInTheDocument();
  });

  it("al volver al menú Gestión/Ayuda no muestra Terminar chat", async () => {
    vi.mocked(searchTramites).mockResolvedValue({
      items: [
        {
          id: "11111111-1111-4111-a111-111111111111",
          radicado: "R-2026-001",
          fecha: "01/03/2026 10:00",
          estado: "borrador",
          placa: "ABC123",
          vin: "VIN1",
          tipoTramite: "Traspaso",
          compania: null,
          href: "/tramites/11111111-1111-4111-a111-111111111111",
        },
      ],
      total: 1,
    });

    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Buscar por placa/i }));
    await user.type(
      screen.getByPlaceholderText("Pregúntale a DR. FLIT..."),
      "ABC123{Enter}",
    );

    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Volver al menú" })).toBeInTheDocument();
    });
    expect(
      screen.getByRole("button", {
        name: "Terminar chat y borrar la conversación",
      }),
    ).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Volver al menú" }));
    expect(screen.getByLabelText("Sesiones del chat")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", {
        name: "Terminar chat y borrar la conversación",
      }),
    ).not.toBeInTheDocument();
  });

  it("Escape no cierra el panel", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    await user.keyboard("{Escape}");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("el panel no es modal (sistema usable detrás)", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    expect(screen.getByRole("dialog")).toHaveAttribute("aria-modal", "false");
    expect(
      screen.queryByRole("button", { name: "Cerrar asistente" }),
    ).not.toBeInTheDocument();
  });

  it("HU-G — «Necesito ayuda» sugiere el artículo del módulo actual antes de preguntar", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" routeScope="/tramites/nuevo|tramites" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Necesito ayuda/i }));

    // El bot lo nombra en el mensaje y lo ofrece como chip abriendo el manual.
    expect(screen.getByText(/Estás en un módulo con documentación/)).toBeInTheDocument();
    const list = screen.getByLabelText("Artículos del manual");
    await user.click(within(list).getByRole("button", { name: /Cómo crear un trámite/ }));
    expect(window.open).toHaveBeenCalledWith(
      "/manual/1-gestor/2-crear-tramite",
      "_blank",
      "noopener,noreferrer",
    );
    // La pregunta libre sigue abierta: el composer está habilitado.
    expect(screen.getByPlaceholderText("Pregúntale a DR. FLIT...")).toBeEnabled();
  });

  it("HU-G — sin artículo para el lugar, pide la consulta como siempre", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" routeScope="/|log-qx" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Necesito ayuda/i }));

    expect(screen.getByText(/Cuéntame qué necesitas/)).toBeInTheDocument();
    expect(screen.queryByText(/Cómo crear un trámite/)).not.toBeInTheDocument();
  });

  it("HU-F — sin JWT (perfil gestor) la ayuda no devuelve artículos del OT", async () => {
    const user = userEvent.setup();
    render(<DrFlitAssistant displayName="Juan" />);
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.click(screen.getByRole("button", { name: /Necesito ayuda/i }));
    await user.type(
      screen.getByPlaceholderText("Pregúntale a DR. FLIT..."),
      "reglas del organismo{Enter}",
    );

    await waitFor(() => {
      expect(screen.getByText(/No encontré un artículo|Encontré/)).toBeInTheDocument();
    });
    expect(screen.queryByText("Reglas del Organismo")).not.toBeInTheDocument();
  });
});
