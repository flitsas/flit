import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { ChipFiltro, ChipsFiltro } from "@flit/ui/ChipsFiltro";
import { rutaIlustracion } from "@flit/ui/ilustraciones";
import { KpiCard } from "@/components/atom/modules/_reportes/KpiCard";

// HU #14476 — piezas del prototipo «Diseño Inteligente»: tarjeta de cifra (`.metrica`) y chips de conteo (`.chip`).

describe("TarjetaMetrica", () => {
  it("muestra etiqueta, cifra, ilustración decorativa y pie", () => {
    const { container } = render(
      <TarjetaMetrica etiqueta="Entregados" cifra="1.204" ilustracion={rutaIlustracion("tramites")} pie="Ver el detalle" />,
    );
    expect(screen.getByText("Entregados")).toBeInTheDocument();
    expect(screen.getByText("1.204")).toBeInTheDocument();
    expect(screen.getByText("Ver el detalle")).toBeInTheDocument();
    const img = container.querySelector("img");
    // UI Kit 2026 (propuesta-2): la ruta de la ilustración apunta al ícono del kit en su disco.
    expect(img).toHaveAttribute("src", "/assets/kit/modulo/tramites.svg");
    expect(img).toHaveAttribute("alt", "");
  });

  it("sin acción es una tarjeta quieta (ni enlace ni botón)", () => {
    render(<TarjetaMetrica etiqueta="A" cifra="1" />);
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("con href es un enlace y con onClick un botón", () => {
    const onClick = vi.fn();
    const { rerender } = render(<TarjetaMetrica etiqueta="A" cifra="1" href="/reportes" />);
    expect(screen.getByRole("link")).toHaveAttribute("href", "/reportes");
    rerender(<TarjetaMetrica etiqueta="A" cifra="1" onClick={onClick} />);
    fireEvent.click(screen.getByRole("button"));
    expect(onClick).toHaveBeenCalledTimes(1);
  });
});

describe("KpiCard sobre TarjetaMetrica", () => {
  it("conserva tooltip, testId y la variación en el pie", () => {
    render(<KpiCard label="Aprobados" value="12" tooltip="cómo se calcula" variation={null} testId="kpi" />);
    const tarjeta = screen.getByTestId("kpi");
    expect(tarjeta).toHaveAttribute("title", "cómo se calcula");
    expect(tarjeta).toHaveTextContent("Aprobados");
    expect(tarjeta).toHaveTextContent("12");
    expect(tarjeta).toHaveTextContent("Sin base de comparación");
  });
});

describe("ChipsFiltro", () => {
  it("marca el activo con aria-pressed y avisa el cambio", () => {
    const onChange = vi.fn();
    render(
      <ChipsFiltro
        ariaLabel="Filtrar por estado"
        valor="todos"
        onChange={onChange}
        opciones={[
          { valor: "todos", etiqueta: "Todos", cifra: 15 },
          { valor: "borrador", etiqueta: "Borrador", cifra: 2 },
          { valor: "vacio", etiqueta: "Vacío", cifra: 0, disabled: true },
        ]}
      />,
    );
    expect(screen.getByRole("group", { name: "Filtrar por estado" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /15\s*Todos/ })).toHaveAttribute("aria-pressed", "true");
    const borrador = screen.getByRole("button", { name: /2\s*Borrador/ });
    expect(borrador).toHaveAttribute("aria-pressed", "false");
    fireEvent.click(borrador);
    expect(onChange).toHaveBeenCalledWith("borrador");
    expect(screen.getByRole("button", { name: /Vacío/ })).toBeDisabled();
  });

  it("el chip suelto funciona sin cifra", () => {
    const onClick = vi.fn();
    render(<ChipFiltro etiqueta="Hoy" activo={false} onClick={onClick} />);
    fireEvent.click(screen.getByRole("button", { name: "Hoy" }));
    expect(onClick).toHaveBeenCalled();
  });
});
