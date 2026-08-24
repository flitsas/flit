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
