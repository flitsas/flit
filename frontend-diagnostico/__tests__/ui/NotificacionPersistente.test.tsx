import { describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { NotificacionPersistente } from "@flit/ui/NotificacionPersistente";

// Uso de ejemplo: <NotificacionPersistente titulo="ZIP de evidencias listo" mensaje="Lote Flota norte"
//   accion={{ label: "Descargar", onClick: descargar }} onCerrar={cerrar} />

describe("NotificacionPersistente (HU #14562 · UI-1)", () => {
  it("es un status con título, mensaje y acción", async () => {
    const onClick = vi.fn();
    render(
      <NotificacionPersistente titulo="ZIP listo" mensaje="Lote A" accion={{ label: "Descargar", onClick }} onCerrar={vi.fn()} />,
    );
    const s = await screen.findByRole("status");
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
    fireEvent.click(screen.getByRole("button", { name: "Cerrar notificación" }));
    vi.useRealTimers();
    expect(onCerrar).toHaveBeenCalledTimes(1);
  });

  it("la acción puede ser un enlace con href", async () => {
    render(<NotificacionPersistente titulo="ZIP listo" accion={{ label: "Descargar", href: "/zip" }} onCerrar={vi.fn()} />);
    expect(await screen.findByRole("link", { name: "Descargar" })).toHaveAttribute("href", "/zip");
  });
});

// HU #14562 · M11 (auditoría): la región viva existe vacía antes de recibir el texto; si nace con él, NVDA, JAWS y
// VoiceOver no lo anuncian de forma fiable.
describe("NotificacionPersistente: región viva diferida (HU #14562 · M11)", () => {
  it("M11: se monta como región aria-live vacía y el contenido llega después", async () => {
    const { container } = render(<NotificacionPersistente titulo="ZIP listo" mensaje="Lote A" onCerrar={vi.fn()} />);
    const region = container.firstElementChild as HTMLElement;
    expect(region).toHaveAttribute("aria-live", "polite");
    expect(region).toBeEmptyDOMElement();
    expect(await screen.findByRole("status")).toBe(region);
    expect(region).toHaveTextContent("ZIP listo");
  });
});
