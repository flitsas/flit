// HU #13248b (F9 #13245) — selector múltiple con buscador: chips con ✕, contador, búsqueda por nombre o
// NIT (ignora puntos y guion), lista completa con scroll, «Seleccionar las filtradas», «Limpiar», modo
// chips con pocas opciones y los estados cargando / error / vacío.

import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  MultiSelectBuscable,
  coincideBusqueda,
  type OpcionSeleccionable,
} from "../MultiSelectBuscable";

const opcion = (i: number): OpcionSeleccionable => ({
  id: `c-${i}`,
  label: `Compañía ${String(i).padStart(3, "0")}`,
  detalle: `NIT 900${String(i).padStart(3, "0")}-1`,
  codigo: `900${String(i).padStart(3, "0")}-1`,
});
const muchas = Array.from({ length: 364 }, (_, i) => opcion(i + 1));

function Arnes(props: Partial<React.ComponentProps<typeof MultiSelectBuscable>>) {
  const [sel, setSel] = useState<OpcionSeleccionable[]>(props.seleccion ?? []);
  return (
    <MultiSelectBuscable
      testId="sel"
      grupo="Compañías"
      opciones={muchas}
      buscarLabel="Buscar compañía"
      {...props}
      seleccion={sel}
      onChange={setSel}
    />
  );
}

describe("coincideBusqueda", () => {
  it("ignora tildes, mayúsculas, puntos y guion del NIT", () => {
    const o: OpcionSeleccionable = { id: "1", label: "Bogotá Motors SAS", codigo: "900.123.456-7" };
    expect(coincideBusqueda(o, "bogota")).toBe(true);
    expect(coincideBusqueda(o, "900123456")).toBe(true);
    expect(coincideBusqueda(o, "900.123-4")).toBe(true);
    expect(coincideBusqueda(o, "999")).toBe(false);
    expect(coincideBusqueda(o, "   ")).toBe(true);
  });
});

describe("HU #13248b — selector con buscador", () => {
  it("muestra la lista COMPLETA con scroll, sin paginación", () => {
    render(<Arnes />);
    expect(within(screen.getByTestId("sel-lista")).getAllByRole("checkbox")).toHaveLength(364);
    expect(screen.queryByText(/Mostrando/)).not.toBeInTheDocument();
    expect(screen.getByText("364 en total")).toBeInTheDocument();
    expect(screen.getByTestId("sel-lista").className).toMatch(/overflow-y-auto/);
  });

  it("filtra mientras se escribe, por nombre o NIT", async () => {
    const user = userEvent.setup();
    render(<Arnes />);
    await user.type(screen.getByLabelText("Buscar compañía"), "900.012");
    const filas = within(screen.getByTestId("sel-lista")).getAllByRole("checkbox");
    expect(filas).toHaveLength(1);
    expect(screen.getByText("1 de 364")).toBeInTheDocument();
    await user.clear(screen.getByLabelText("Buscar compañía"));
    await user.type(screen.getByLabelText("Buscar compañía"), "zzz");
    expect(screen.getByText("Sin resultados")).toBeInTheDocument();
  });

  it("marca, cuenta, quita con la ✕ y conserva lo marcado al cambiar la búsqueda", async () => {
    const user = userEvent.setup();
    render(<Arnes />);
    await user.click(screen.getByRole("checkbox", { name: "Compañía 001 (NIT 900001-1)" }));
    expect(screen.getByText("1 seleccionada")).toBeInTheDocument();
    await user.type(screen.getByLabelText("Buscar compañía"), "Compañía 002");
    await user.click(screen.getByRole("checkbox", { name: "Compañía 002 (NIT 900002-1)" }));
    expect(screen.getByText("2 seleccionadas")).toBeInTheDocument();
    const chips = screen.getByRole("list", { name: "Compañías seleccionadas" });
    expect(within(chips).getAllByRole("listitem")).toHaveLength(2);
    await user.click(screen.getByRole("button", { name: "Quitar Compañía 001" }));
    expect(screen.getByText("1 seleccionada")).toBeInTheDocument();
  });

  it("«Seleccionar las filtradas» marca solo lo que se ve y «Limpiar» deja todo en cero", async () => {
    const user = userEvent.setup();
    render(<Arnes />);
    await user.type(screen.getByLabelText("Buscar compañía"), "Compañía 01");
    // 010 a 019 = 10 coincidencias.
    await user.click(screen.getByRole("button", { name: "Seleccionar las filtradas" }));
    expect(screen.getByText("10 seleccionadas")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Seleccionar las filtradas" })).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "Limpiar" }));
    expect(screen.getByText("0 seleccionadas")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Limpiar" })).toBeDisabled();
  });

  it("el contador concuerda en género con `genero`", () => {
    render(<Arnes genero="m" seleccion={[opcion(1)]} />);
    expect(screen.getByText("1 seleccionado")).toBeInTheDocument();
  });

  it("muestra el motivo del rechazo junto a la opción y marca su chip", () => {
    render(<Arnes seleccion={[opcion(1)]} errores={{ "c-1": "Está inactiva." }} />);
    expect(screen.getByTestId("sel-errores")).toHaveTextContent("Compañía 001: Está inactiva.");
  });
});

describe("HU #13248b — modo chips (pocas opciones)", () => {
  const pocas = muchas.slice(0, 3);

  it("con ≤ chipsHasta opciones se ven como chips conmutables, sin buscador", async () => {
    const user = userEvent.setup();
    render(<Arnes opciones={pocas} chipsHasta={8} />);
    expect(screen.queryByLabelText("Buscar compañía")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Limpiar" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("checkbox", { name: "Compañía 002 (NIT 900002-1)" }));
    expect(screen.getByRole("checkbox", { name: "Compañía 002 (NIT 900002-1)" })).toBeChecked();
    expect(screen.getByText("1 seleccionada")).toBeInTheDocument();
  });

  it("con más de chipsHasta opciones usa el selector con buscador", () => {
    render(<Arnes opciones={muchas.slice(0, 9)} chipsHasta={8} />);
    expect(screen.getByLabelText("Buscar compañía")).toBeInTheDocument();
  });
});

describe("HU #13248b — estados", () => {
  it("cargando", () => {
    render(<Arnes opciones={[]} loading textoCargando="Cargando compañías…" />);
    expect(screen.getByRole("status")).toHaveTextContent("Cargando compañías…");
  });

  it("error con Reintentar", async () => {
    const user = userEvent.setup();
    const onRetry = vi.fn();
    render(<Arnes opciones={[]} error onRetry={onRetry} textoError="No se pudo." />);
    expect(screen.getByRole("alert")).toHaveTextContent("No se pudo.");
    await user.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it("vacío", () => {
    render(<Arnes opciones={[]} textoVacio="No hay compañías." />);
    expect(screen.getByRole("status")).toHaveTextContent("No hay compañías.");
  });
});

describe("listas largas: protecciones de uso", () => {
  it("con muchas opciones, «Seleccionar las filtradas» no deja marcar todo sin escribir antes en el buscador", async () => {
    const user = userEvent.setup();
    render(<Arnes />);
    const boton = screen.getByRole("button", { name: "Seleccionar las filtradas" });
    expect(boton).toBeDisabled();
    expect(boton).toHaveAttribute("title", expect.stringMatching(/escribe en el buscador/i));
    await user.type(screen.getByLabelText("Buscar compañía"), "Compañía 00");
    expect(boton).toBeEnabled();
    await user.click(boton);
    expect(screen.getByText("9 seleccionadas")).toBeInTheDocument();
  });

  it("con pocas opciones se puede seleccionar todo sin filtrar", async () => {
    const user = userEvent.setup();
    render(<Arnes opciones={muchas.slice(0, 12)} />);
    const boton = screen.getByRole("button", { name: "Seleccionar las filtradas" });
    expect(boton).toBeEnabled();
    await user.click(boton);
    expect(screen.getByText("12 seleccionadas")).toBeInTheDocument();
  });

  it("los chips de lo seleccionado tienen alto máximo con scroll, para no empujar la lista", async () => {
    const user = userEvent.setup();
    render(<Arnes seleccion={muchas.slice(0, 40)} />);
    const chips = screen.getByTestId("sel-seleccion");
    expect(chips.className).toMatch(/max-h-24/);
    expect(chips.className).toMatch(/overflow-y-auto/);
    await user.type(screen.getByLabelText("Buscar compañía"), "x");
  });
});
