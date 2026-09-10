// HU-03 (Feature #12201) — CF-18/CF-21: los filtros del historial y las TRES opciones del
// selector de estado.
// Uso de ejemplo: render(<HistorialFilters value={HISTORIAL_FILTERS_EMPTY} onChange={fn} users={[]} />)
import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import {
  HISTORIAL_FILTERS_EMPTY,
  HistorialFilters,
  hasHistorialFilters,
} from "../HistorialFilters";

const users = [
  { id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", name: "Ana Gestora" },
  { id: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", name: "Beto Analista" },
];

function renderFilters(overrides = {}) {
  const onChange = vi.fn();
  render(
    <HistorialFilters
      value={HISTORIAL_FILTERS_EMPTY}
      onChange={onChange}
      users={users}
      {...overrides}
    />,
  );
  return { onChange };
}

describe("HistorialFilters — CF-18", () => {
  it("el filtro de estado ofrece exactamente tres opciones de usuario, más «Todos»", () => {
    renderFilters();

    const estado = screen.getByLabelText("Estado");
    const opciones = within(estado).getAllByRole("option").map((o) => o.textContent);

    expect(opciones).toEqual(["Todos", "Generado", "Error", "En proceso"]);
    // Cuatro estados internos, tres etiquetas: pending y processing no aparecen sueltos.
    expect(opciones).not.toContain("Pendiente");
    expect(opciones).not.toContain("Procesando");
  });

  it("ofrece los tres filtros del AC: tipo de documento, rango de fechas y usuario", () => {
    renderFilters();

    expect(screen.getByLabelText("Tipo de documento")).toBeInTheDocument();
    expect(screen.getByLabelText("Desde")).toBeInTheDocument();
    expect(screen.getByLabelText("Hasta")).toBeInTheDocument();
    expect(screen.getByLabelText("Usuario")).toBeInTheDocument();
  });

  it("propaga el tipo de documento seleccionado sin tocar los demás filtros", async () => {
    const { onChange } = renderFilters();

    await userEvent.selectOptions(screen.getByLabelText("Tipo de documento"), "certificado_rues");

    expect(onChange).toHaveBeenCalledWith({
      ...HISTORIAL_FILTERS_EMPTY,
      documentType: "certificado_rues",
    });
  });

  it("propaga la opción de estado como opción de USUARIO, no como estado interno", async () => {
    const { onChange } = renderFilters();

    await userEvent.selectOptions(screen.getByLabelText("Estado"), "en_proceso");

    expect(onChange).toHaveBeenCalledWith({ ...HISTORIAL_FILTERS_EMPTY, status: "en_proceso" });
  });

  it("lista como opciones de usuario los autores recibidos", () => {
    renderFilters();

    const usuario = screen.getByLabelText("Usuario");
    expect(within(usuario).getAllByRole("option").map((o) => o.textContent)).toEqual([
      "Todos",
      "Ana Gestora",
      "Beto Analista",
    ]);
  });

  it("sin autores conocidos, el selector de usuario queda deshabilitado y no miente", () => {
    renderFilters({ users: [] });

    expect(screen.getByLabelText("Usuario")).toBeDisabled();
  });

  it("«Limpiar filtros» solo aparece cuando hay algún filtro activo", async () => {
    const onChange = vi.fn();
    const { rerender } = render(
      <HistorialFilters value={HISTORIAL_FILTERS_EMPTY} onChange={onChange} users={users} />,
    );

    expect(screen.queryByRole("button", { name: /limpiar filtros/i })).not.toBeInTheDocument();

    rerender(
      <HistorialFilters
        value={{ ...HISTORIAL_FILTERS_EMPTY, status: "error" }}
        onChange={onChange}
        users={users}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: /limpiar filtros/i }));
    expect(onChange).toHaveBeenCalledWith(HISTORIAL_FILTERS_EMPTY);
  });

  it("accesibilidad: cada control tiene label asociada por htmlFor (CF-22)", () => {
    renderFilters();

    for (const etiqueta of ["Tipo de documento", "Estado", "Desde", "Hasta", "Usuario"]) {
      expect(screen.getByLabelText(etiqueta)).toHaveAttribute("id");
    }
  });
});

describe("hasHistorialFilters", () => {
  it("distingue el estado sin filtros del estado con al menos uno", () => {
    expect(hasHistorialFilters(HISTORIAL_FILTERS_EMPTY)).toBe(false);
    expect(hasHistorialFilters({ ...HISTORIAL_FILTERS_EMPTY, dateFrom: "2026-09-01" })).toBe(true);
  });
});
