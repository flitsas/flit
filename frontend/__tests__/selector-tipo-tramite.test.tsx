import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";

const mocks = vi.hoisted(() => ({ listPublishedProcedureTypes: vi.fn() }));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: { listPublishedProcedureTypes: mocks.listPublishedProcedureTypes },
}));

import { SelectorTipoTramite } from "@/components/operacion/SelectorTipoTramite";

function tipo(
  code: string,
  name: string,
  family: ProcedureTypeSummary["family"],
  wizardEnabled = true,
): ProcedureTypeSummary {
  return {
    id: code,
    code,
    name,
    family,
    publicationStatus: "published",
    isActive: true,
    wizardEnabled,
    publishedAt: null,
  };
}

/**
 * ADR-0050 — el selector sustituye a las tres tarjetas fijas del paso 1. Lo que se prueba aquí es
 * que las opciones salen del catálogo y no de una lista escrita a mano, y que la barrera de
 * operación decide qué se puede elegir.
 */
describe("SelectorTipoTramite", () => {
  beforeEach(() => {
    mocks.listPublishedProcedureTypes.mockReset();
  });

  it("agrupa por familia y solo ofrece las que tienen tipos habilitados", async () => {
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo("MATRICULA_NUEVA", "Matrícula inicial", "MATRICULAS"),
      tipo("BLINDAJE", "Blindaje", "OTROS"),
      tipo("CAMBIO_COLOR", "Cambio de color", "OTROS"),
    ]);

    render(<SelectorTipoTramite onElegir={vi.fn()} />);

    expect(await screen.findByText("Matrículas")).toBeInTheDocument();
    expect(screen.getByText("Otros trámites")).toBeInTheDocument();
    // Traspaso no aparece: no hay ningún tipo habilitado en esa familia.
    expect(screen.queryByText("Traspaso")).not.toBeInTheDocument();
  });

  it("no ofrece tipos sin la barrera de operación encendida", async () => {
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo("MATRICULA_NUEVA", "Matrícula inicial", "MATRICULAS"),
      tipo("REMATRICULA", "Rematrícula", "MATRICULAS", false),
    ]);

    render(<SelectorTipoTramite onElegir={vi.fn()} />);

    await userEvent.click(await screen.findByText("Matrículas"));

    expect(await screen.findByText("Matrícula inicial")).toBeInTheDocument();
    expect(screen.queryByText("Rematrícula")).not.toBeInTheDocument();
  });

  it("devuelve el code del tipo elegido, no la modalidad", async () => {
    const onElegir = vi.fn();
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo("LEVANTAMIENTO_PRENDA", "Levantamiento de prenda", "OTROS"),
    ]);

    render(<SelectorTipoTramite onElegir={onElegir} />);

    await userEvent.click(await screen.findByText("Otros trámites"));
    await userEvent.click(await screen.findByText("Levantamiento de prenda"));

    expect(onElegir).toHaveBeenCalledWith("LEVANTAMIENTO_PRENDA");
  });

  it("deshabilita la familia que la compañía tiene bloqueada", async () => {
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo("BLINDAJE", "Blindaje", "OTROS"),
    ]);

    render(<SelectorTipoTramite onElegir={vi.fn()} bloqueadas={{ otros: true }} />);

    const boton = await screen.findByRole("button", { name: /Otros trámites/ });
    expect(boton).toBeDisabled();
    expect(boton).toHaveTextContent(/no habilitado para tu compañía/);
  });

  it("cuando ningún tipo está habilitado lo dice, en vez de mostrar un selector vacío", async () => {
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo("BLINDAJE", "Blindaje", "OTROS", false),
    ]);

    render(<SelectorTipoTramite onElegir={vi.fn()} />);

    await waitFor(() =>
      expect(screen.getByText(/No hay tipos de trámite habilitados/)).toBeInTheDocument(),
    );
  });
});

/**
 * Composición de la pantalla: la propuesta la presenta como UN bloque —cabecera con título y
 * subtítulo, rejilla de tarjetas y salida— y no como un encabezado suelto con una lista debajo.
 */
describe("SelectorTipoTramite — composición", () => {
  beforeEach(() => {
    mocks.listPublishedProcedureTypes.mockReset();
  });

  it("encabeza la elección con el título y el subtítulo del sistema", async () => {
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo("MATRICULA_NUEVA", "Matrícula inicial", "MATRICULAS"),
    ]);

    render(<SelectorTipoTramite onElegir={vi.fn()} />);

    expect(await screen.findByText("Selecciona el tipo de trámite")).toBeInTheDocument();
    expect(
      screen.getByText("Define el trámite principal que se radicará con este expediente."),
    ).toBeInTheDocument();
  });

  it("el paso de tipos también trae cabecera, no una lista pelada", async () => {
    // Era el paso más flojo: en OTROS son quince trámites y solo se veía el nombre, sin contexto de
    // qué familia se está recorriendo ni cómo volver.
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo("BLINDAJE", "Blindaje", "OTROS"),
      tipo("CAMBIO_COLOR", "Cambio de color", "OTROS"),
    ]);
    const user = userEvent.setup();

    render(<SelectorTipoTramite onElegir={vi.fn()} />);
    await user.click(await screen.findByRole("button", { name: /Otros trámites/ }));

    expect(screen.getByRole("heading", { name: "Otros trámites" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Blindaje" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cambio de color" })).toBeInTheDocument();
  });

  it("desde los tipos se puede volver a las familias", async () => {
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo("BLINDAJE", "Blindaje", "OTROS"),
    ]);
    const user = userEvent.setup();

    render(<SelectorTipoTramite onElegir={vi.fn()} />);
    await user.click(await screen.findByRole("button", { name: /Otros trámites/ }));
    await user.click(screen.getByRole("button", { name: /Volver a las familias/ }));

    expect(screen.getByText("Selecciona el tipo de trámite")).toBeInTheDocument();
  });

  it("ofrece salir sin elegir solo si el contenedor sabe a dónde volver", async () => {
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo("MATRICULA_NUEVA", "Matrícula inicial", "MATRICULAS"),
    ]);
    const onCancelar = vi.fn();
    const user = userEvent.setup();

    const { unmount } = render(<SelectorTipoTramite onElegir={vi.fn()} onCancelar={onCancelar} />);
    await user.click(await screen.findByRole("button", { name: "Cancelar" }));
    expect(onCancelar).toHaveBeenCalledOnce();
    unmount();

    render(<SelectorTipoTramite onElegir={vi.fn()} />);
    await screen.findByText("Selecciona el tipo de trámite");
    expect(screen.queryByRole("button", { name: "Cancelar" })).not.toBeInTheDocument();
  });

  it("una familia bloqueada se ve y dice por qué, en vez de desaparecer", async () => {
    // Desaparecerla haría creer que el trámite no existe, en vez de que la compañía no lo tiene
    // habilitado. Y el motivo va en el texto: un estado nunca depende solo del color.
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo("BLINDAJE", "Blindaje", "OTROS"),
    ]);

    render(<SelectorTipoTramite onElegir={vi.fn()} bloqueadas={{ otros: true }} />);

    const opcion = await screen.findByRole("button", { name: /Otros trámites/ });
    expect(opcion).toBeDisabled();
    expect(screen.getByText(/no habilitado para tu compañía/)).toBeInTheDocument();
  });
});

/**
 * Presentado en modal: la cabecera del diálogo ya dice título y subtítulo, y de ella cuelga el
 * `aria-labelledby`. El selector no debe repetirlos dentro.
 */
describe("SelectorTipoTramite — título en el contenedor", () => {
  beforeEach(() => {
    mocks.listPublishedProcedureTypes.mockReset();
  });

  it("calla su cabecera cuando el contenedor ya la pinta", async () => {
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo("MATRICULA_NUEVA", "Matrícula inicial", "MATRICULAS"),
    ]);

    render(<SelectorTipoTramite onElegir={vi.fn()} tituloEnContenedor />);

    await screen.findByRole("button", { name: /Matrículas/ });
    expect(screen.queryByText("Selecciona el tipo de trámite")).not.toBeInTheDocument();
  });

  it("pero conserva la del paso de tipos, que nombra la familia y no es un duplicado", async () => {
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo("BLINDAJE", "Blindaje", "OTROS"),
    ]);
    const user = userEvent.setup();

    render(<SelectorTipoTramite onElegir={vi.fn()} tituloEnContenedor />);
    await user.click(await screen.findByRole("button", { name: /Otros trámites/ }));

    expect(screen.getByRole("heading", { name: "Otros trámites" })).toBeInTheDocument();
  });
});
