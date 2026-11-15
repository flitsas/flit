import { describe, expect, it } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { BarrasSerie } from "@/components/estadisticas/graficas/BarrasSerie";
import { BarraApilada } from "@/components/estadisticas/graficas/BarraApilada";
import { ListaBarras } from "@/components/estadisticas/graficas/ListaBarras";

// Uso de ejemplo: <BarrasSerie titulo="Uso en el tiempo" subtitulo="por día" etiquetas={["14/11","15/11"]}
// series={[{ nombre: "Diagnósticos", color: "var(--chart-1)", valores: [2, 0] }]} /> pinta columnas aria-hidden y una
// tabla alternativa con los mismos datos (spec-ui §4.6: figure + figcaption + tabla, nunca solo color).

const SERIES = [
  { nombre: "Diagnósticos", color: "var(--chart-1)", valores: [2, 0] },
  { nombre: "Vehículos", color: "var(--chart-2)", valores: [1080, 0] },
];

describe("BarrasSerie — columnas agrupadas accesibles", () => {
  it("es una figura con su título visible y una tabla alternativa con todos los valores", () => {
    render(<BarrasSerie titulo="Uso en el tiempo" subtitulo="por día" etiquetas={["14/11", "15/11"]} series={SERIES} />);
    const figura = screen.getByRole("figure", { name: "Uso en el tiempo" });
    const tabla = within(figura).getByRole("table", { name: /Uso en el tiempo/ });
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Periodo", "Diagnósticos", "Vehículos"]);
    const filas = within(tabla).getAllByRole("row").slice(1);
    expect(filas.map((f) => within(f).getAllByRole("cell").map((c) => c.textContent))).toEqual([
      ["2", "1.080"],
      ["0", "0"],
    ]);
    expect(within(filas[0]).getByRole("rowheader")).toHaveTextContent("14/11");
    expect(within(figura).getByText("por día")).toBeInTheDocument();
  });

  it("la tabla de valores es visible en un desplegable «Ver datos» (UA-3, quien ve la pantalla también lee las cifras)", () => {
    render(<BarrasSerie titulo="Uso en el tiempo" etiquetas={["14/11", "15/11"]} series={SERIES} />);
    const figura = screen.getByRole("figure", { name: "Uso en el tiempo" });
    const resumen = within(figura).getByText("Ver datos");
    expect(resumen.tagName).toBe("SUMMARY");
    const desplegable = resumen.closest("details") as HTMLDetailsElement;
    expect(desplegable.open).toBe(false);
    fireEvent.click(resumen);
    expect(desplegable.open).toBe(true);
    const tabla = within(desplegable).getByRole("table", { name: /Uso en el tiempo/ });
    expect(tabla).not.toHaveClass("sr-only");
    expect(within(tabla).getByText("1.080")).toBeInTheDocument();
  });

  it("las barras son decorativas (aria-hidden) y cada una lleva su valor en title", () => {
    const { container } = render(<BarrasSerie titulo="Uso" etiquetas={["14/11"]} series={SERIES} />);
    const grafico = container.querySelector("[data-parte='barras']");
    expect(grafico).toHaveAttribute("aria-hidden", "true");
    expect(container.querySelector("[title='Vehículos · 14/11: 1.080']")).not.toBeNull();
  });

  it("la leyenda nombra cada serie en texto (no solo color)", () => {
    render(<BarrasSerie titulo="Uso" etiquetas={["14/11"]} series={SERIES} />);
    const leyenda = screen.getByRole("list", { name: "Leyenda" });
    expect(within(leyenda).getAllByRole("listitem").map((i) => i.textContent)).toEqual(["Diagnósticos", "Vehículos"]);
  });

  it("con todos los valores en cero no divide por cero ni lanza", () => {
    expect(() =>
      render(<BarrasSerie titulo="Uso" etiquetas={["14/11"]} series={[{ nombre: "A", color: "var(--chart-1)", valores: [0] }]} />),
    ).not.toThrow();
  });
});

describe("BarraApilada y ListaBarras — valores en texto y tabla alternativa", () => {
  it("BarraApilada muestra cifra y porcentaje de cada segmento en la leyenda y en la tabla", () => {
    render(
      <BarraApilada
        titulo="Veredictos"
        segmentos={[
          { nombre: "Viables", valor: 75, color: "var(--di-success-dot)" },
          { nombre: "Bloqueados", valor: 25, color: "var(--di-danger)" },
        ]}
      />,
    );
    const figura = screen.getByRole("figure", { name: "Veredictos" });
    expect(within(figura).getByText("Viables: 75 (75 %)")).toBeInTheDocument();
    const tabla = within(figura).getByRole("table");
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Categoría", "Cantidad", "Porcentaje"]);
  });

  it("ListaBarras pinta una fila por ítem con su cifra visible y la tabla alternativa", () => {
    render(
      <ListaBarras
        titulo="Saneamientos"
        items={[
          { clave: "s", nombre: "Solicitado", valor: 3 },
          { clave: "n", nombre: "Saneado", valor: 0 },
        ]}
      />,
    );
    const figura = screen.getByRole("figure", { name: "Saneamientos" });
    expect(within(figura).getByRole("table")).toBeInTheDocument();
    expect(within(figura).getAllByText("3").length).toBeGreaterThan(0);
  });

  it("ListaBarras con acción por ítem expone botones con nombre accesible", () => {
    const elegidos: string[] = [];
    render(
      <ListaBarras
        titulo="Top"
        items={[{ clave: "e1", nombre: "Compañía Demo", valor: 4, detalle: "120 vehículos" }]}
        onElegir={(c) => elegidos.push(c)}
        etiquetaAccion={(i) => `Ver ${i.nombre}`}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Ver Compañía Demo" }));
    expect(elegidos).toEqual(["e1"]);
  });
});
