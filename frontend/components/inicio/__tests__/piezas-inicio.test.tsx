import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";

vi.mock("@/hooks/useDominantColor", () => ({
  useDominantColor: () => null,
  bannerAmbientGradient: () => "none",
}));

import { PanelInicio } from "../PanelInicio";
import { GraficoArea } from "../GraficoArea";
import { BarrasApiladas } from "../BarrasApiladas";
import { CarruselInicio } from "../CarruselInicio";

afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("PanelInicio", () => {
  it("nombra la sección por su título y pinta subtítulo y acción", () => {
    render(
      <PanelInicio titulo="Cola de gestión" subtitulo="5 con mayor inactividad" accion={<a href="#ver-todos">Ver todos</a>}>
        <p>contenido</p>
      </PanelInicio>,
    );
    const seccion = screen.getByRole("region", { name: "Cola de gestión" });
    expect(within(seccion).getByText("5 con mayor inactividad")).toBeInTheDocument();
    expect(within(seccion).getByRole("link", { name: "Ver todos" })).toHaveAttribute("href", "#ver-todos");
    expect(within(seccion).getByText("contenido")).toBeInTheDocument();
  });
});

describe("GraficoArea", () => {
  it("resume cada punto para el lector de pantalla con el formato pedido", () => {
    render(
      <GraficoArea
        nombreSerie="Tiempo promedio de decisión"
        formato={(v) => `${v} d`}
        puntos={[
          { etiqueta: "16 nov", valor: 2 },
          { etiqueta: "17 nov", valor: 3 },
        ]}
      />,
    );
    expect(screen.getByText("16 nov: 2 d")).toBeInTheDocument();
    expect(screen.getByText("17 nov: 3 d")).toBeInTheDocument();
  });
});

describe("BarrasApiladas", () => {
  const series = [
    { clave: "completados", etiqueta: "Completados", color: "var(--chart-4)" },
    { clave: "enProceso", etiqueta: "En proceso", color: "var(--chart-1)" },
    { clave: "rechazados", etiqueta: "Rechazados", color: "var(--chart-5)" },
  ];

  it("lleva la leyenda de las series y el resumen de cada fila", () => {
    render(
      <BarrasApiladas
        series={series}
        filas={[{ etiqueta: "Matrícula inicial", completados: 980, enProceso: 310, rechazados: 64 }]}
      />,
    );
    expect(screen.getByText("Completados")).toBeInTheDocument();
    expect(screen.getByText("Matrícula inicial: Completados 980, En proceso 310, Rechazados 64")).toBeInTheDocument();
  });
});

describe("CarruselInicio", () => {
  it("muestra el slide fijo primero, con su antetítulo, y suma un punto por banner", () => {
    render(
      <CarruselInicio
        bienvenida={[{ antetitulo: "Tu operación, sin pausas", titulo: "Hola, Carolina", cuerpo: "Tienes 8 trámites" }]}
        banners={[{ id: "b1", name: "Aviso uno", linkUrl: null } as never]}
      />,
    );
    expect(screen.getByRole("heading", { name: "Hola, Carolina" })).toBeInTheDocument();
    expect(screen.getByText("Tu operación, sin pausas")).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: /^Slide \d/ })).toHaveLength(2);
  });

  it("avanza solo cada 6 s y con las flechas", () => {
    vi.useFakeTimers();
    render(
      <CarruselInicio
        bienvenida={[
          { titulo: "Uno", cuerpo: "a" },
          { titulo: "Dos", cuerpo: "b" },
        ]}
        banners={[]}
      />,
    );
    expect(screen.getByRole("heading", { name: "Uno" })).toBeInTheDocument();
    act(() => vi.advanceTimersByTime(6000));
    expect(screen.getByRole("heading", { name: "Dos" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Siguiente" }));
    expect(screen.getByRole("heading", { name: "Uno" })).toBeInTheDocument();
  });
});
