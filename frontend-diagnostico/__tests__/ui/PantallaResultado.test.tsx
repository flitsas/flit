import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { PantallaResultado } from "@flit/ui/PantallaResultado";

// Uso de ejemplo: <PantallaResultado tono="exito" titulo="Diagnóstico iniciado con 231 vehículos"
//   primaria={{ etiqueta: "Ver tablero", onClick: irAlTablero }} secundaria={{ etiqueta: "Volver", href: "/" }}
//   redireccion={{ segundos: 5, destino: "el tablero", onNavegar: irAlTablero }} />

beforeEach(() => {
  vi.useFakeTimers();
});
afterEach(() => {
  vi.useRealTimers();
});

describe("PantallaResultado (HU #14562 · UI-1, AC18/AC19)", () => {
  it("muestra el título, el mensaje y las dos acciones", () => {
    const ver = vi.fn();
    render(
      <PantallaResultado
        tono="exito"
        titulo="Diagnóstico iniciado con 3 vehículos"
        mensaje="Las consultas ya arrancaron."
        primaria={{ etiqueta: "Ver tablero", onClick: ver }}
        secundaria={{ etiqueta: "Volver", href: "/" }}
      />,
    );
    expect(screen.getByRole("heading", { name: "Diagnóstico iniciado con 3 vehículos" })).toBeInTheDocument();
    expect(screen.getByText("Las consultas ya arrancaron.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Ver tablero" }));
    expect(ver).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("link", { name: "Volver" })).toHaveAttribute("href", "/");
    expect(screen.queryByRole("button", { name: "Quedarme aquí" })).toBeNull();
  });

  it("AC18: con redirección navega una sola vez a los 5 s sin interacción", () => {
    const onNavegar = vi.fn();
    render(
      <PantallaResultado
        tono="exito"
        titulo="Diagnóstico iniciado"
        primaria={{ etiqueta: "Ver tablero", onClick: vi.fn() }}
        redireccion={{ segundos: 5, destino: "el tablero", onNavegar }}
      />,
    );
    act(() => {
      vi.advanceTimersByTime(4999);
    });
    expect(onNavegar).not.toHaveBeenCalled();
    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(onNavegar).toHaveBeenCalledTimes(1);
    act(() => {
      vi.advanceTimersByTime(10_000);
    });
    expect(onNavegar).toHaveBeenCalledTimes(1);
  });

  it("AC19: «Quedarme aquí» cancela la redirección, avisa a onDetener y lo anuncia en aria-live polite", () => {
    const onNavegar = vi.fn();
    const onDetener = vi.fn();
    render(
      <PantallaResultado
        tono="exito"
        titulo="Diagnóstico iniciado"
        primaria={{ etiqueta: "Ver tablero", onClick: vi.fn() }}
        redireccion={{ segundos: 5, destino: "el tablero", onNavegar, onDetener }}
      />,
    );
    const vivo = document.querySelector("[aria-live='polite']") as HTMLElement;
    expect(vivo).not.toBeNull();
    expect(vivo).toHaveTextContent(/el tablero en 5 segundos/);
    act(() => {
      vi.advanceTimersByTime(2000);
    });
    fireEvent.click(screen.getByRole("button", { name: "Quedarme aquí" }));
    act(() => {
      vi.advanceTimersByTime(20_000);
    });
    expect(onNavegar).not.toHaveBeenCalled();
    expect(onDetener).toHaveBeenCalledTimes(1);
    expect(vivo).toHaveTextContent(/detuvimos la redirección/i);
  });

  it("AC19: la cuenta regresiva se ve sin robar el foco ni repetirse en la región viva", () => {
    render(
      <PantallaResultado
        tono="exito"
        titulo="Diagnóstico iniciado"
        primaria={{ etiqueta: "Ver tablero", onClick: vi.fn() }}
        redireccion={{ segundos: 5, destino: "el tablero", onNavegar: vi.fn() }}
      />,
    );
    const antes = document.activeElement;
    const vivo = document.querySelector("[aria-live='polite']") as HTMLElement;
    const textoInicial = vivo.textContent;
    act(() => {
      vi.advanceTimersByTime(2000);
    });
    expect(document.activeElement).toBe(antes);
    expect(vivo.textContent).toBe(textoInicial);
    expect(screen.getByText("3", { selector: "[data-parte='cuenta']" })).toBeInTheDocument();
  });

  it("limpia el temporizador al desmontar", () => {
    const onNavegar = vi.fn();
    const { unmount } = render(
      <PantallaResultado
        tono="exito"
        titulo="Diagnóstico iniciado"
        primaria={{ etiqueta: "Ver tablero", onClick: vi.fn() }}
        redireccion={{ segundos: 5, destino: "el tablero", onNavegar }}
      />,
    );
    act(() => {
      vi.advanceTimersByTime(3000);
    });
    unmount();
    act(() => {
      vi.advanceTimersByTime(10_000);
    });
    expect(onNavegar).not.toHaveBeenCalled();
  });

  it("tono error lo marca con data-tono", () => {
    const { container } = render(
      <PantallaResultado tono="error" titulo="No pudimos iniciar" primaria={{ etiqueta: "Reintentar", onClick: vi.fn() }} />,
    );
    expect(container.querySelector("[data-tono='error']")).not.toBeNull();
  });
});
