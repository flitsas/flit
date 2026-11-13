import { describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { NotificacionPersistente } from "@flit/ui/NotificacionPersistente";

// Uso de ejemplo: <NotificacionPersistente titulo="ZIP de evidencias listo" mensaje="Lote Flota norte"
//   accion={{ label: "Descargar", onClick: descargar }} onCerrar={cerrar} />

describe("NotificacionPersistente (HU #14562 · UI-1)", () => {
  it("es un status con título, mensaje y acción", () => {
    const onClick = vi.fn();
    render(
      <NotificacionPersistente titulo="ZIP listo" mensaje="Lote A" accion={{ label: "Descargar", onClick }} onCerrar={vi.fn()} />,
    );
    const s = screen.getByRole("status");
    expect(s).toHaveTextContent("ZIP listo");
    expect(s).toHaveTextContent("Lote A");
    fireEvent.click(screen.getByRole("button", { name: "Descargar" }));
    expect(onClick).toHaveBeenCalled();
  });

  it("no se cierra sola y «Cerrar» llama onCerrar", () => {
    vi.useFakeTimers();
    const onCerrar = vi.fn();
    render(<NotificacionPersistente titulo="ZIP listo" onCerrar={onCerrar} />);
    act(() => {
      vi.advanceTimersByTime(120_000);
    });
    expect(onCerrar).not.toHaveBeenCalled();
    expect(screen.getByRole("status")).toBeInTheDocument();
    vi.useRealTimers();
    fireEvent.click(screen.getByRole("button", { name: "Cerrar notificación" }));
    expect(onCerrar).toHaveBeenCalledTimes(1);
  });

  it("la acción puede ser un enlace con href", () => {
    render(<NotificacionPersistente titulo="ZIP listo" accion={{ label: "Descargar", href: "/zip" }} onCerrar={vi.fn()} />);
    expect(screen.getByRole("link", { name: "Descargar" })).toHaveAttribute("href", "/zip");
  });
});
