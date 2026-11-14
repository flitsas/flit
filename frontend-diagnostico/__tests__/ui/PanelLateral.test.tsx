import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { PanelLateral } from "@flit/ui/PanelLateral";

// Uso de ejemplo: <PanelLateral abierto={!!vehiculo} onCerrar={cerrar} titulo="MNO789 · Dictamen del vehículo"
//   pie={<Button>Crear trámite</Button>}>…</PanelLateral>

function Arnes({ onCerrar = () => {} }: { onCerrar?: () => void }) {
  const [abierto, setAbierto] = useState(false);
  return (
    <>
      <button onClick={() => setAbierto(true)}>Ver dictamen</button>
      <PanelLateral
        abierto={abierto}
        onCerrar={() => {
          onCerrar();
          setAbierto(false);
        }}
        titulo="ABC123 · Dictamen del vehículo"
        pie={<button>Crear trámite</button>}
      >
        <a href="#x">Detalle</a>
      </PanelLateral>
    </>
  );
}

describe("PanelLateral (HU #14562 · UI-1, AC8)", () => {
  it("cerrado no pinta nada", () => {
    render(<PanelLateral abierto={false} onCerrar={vi.fn()} titulo="X">contenido</PanelLateral>);
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("abierto: dialog con aria-modal, título accesible y 640 px por defecto", () => {
    render(<PanelLateral abierto onCerrar={vi.fn()} titulo="ABC123 · Dictamen">contenido</PanelLateral>);
    const dialogo = screen.getByRole("dialog", { name: "ABC123 · Dictamen" });
    expect(dialogo).toHaveAttribute("aria-modal", "true");
    const panel = dialogo.querySelector("[data-parte='panel']") as HTMLElement;
    expect(panel.style.maxWidth).toBe("640px");
  });

  it("aparece por opacidad, sin escalas, y respeta menos movimiento", () => {
    render(<PanelLateral abierto onCerrar={vi.fn()} titulo="T">contenido</PanelLateral>);
    const panel = screen.getByRole("dialog").querySelector("[data-parte='panel']") as HTMLElement;
    expect(panel.className).toContain("transition-opacity");
    expect(panel.className).toContain("motion-reduce:transition-none");
    expect(panel.className).not.toMatch(/scale-/);
  });

  it("al abrir el foco entra al panel; Tab y Shift+Tab quedan atrapados", () => {
    render(<Arnes />);
    fireEvent.click(screen.getByRole("button", { name: "Ver dictamen" }));
    const dialogo = screen.getByRole("dialog");
    expect(dialogo.contains(document.activeElement)).toBe(true);
    const cerrar = screen.getByRole("button", { name: "Cerrar" });
    const ultimo = screen.getByRole("button", { name: "Crear trámite" });
    ultimo.focus();
    fireEvent.keyDown(document, { key: "Tab" });
    expect(document.activeElement).toBe(cerrar);
    fireEvent.keyDown(document, { key: "Tab", shiftKey: true });
    expect(document.activeElement).toBe(ultimo);
  });

  it("Escape cierra y el foco vuelve al control que lo abrió", () => {
    const onCerrar = vi.fn();
    render(<Arnes onCerrar={onCerrar} />);
    const abrir = screen.getByRole("button", { name: "Ver dictamen" });
    abrir.focus();
    fireEvent.click(abrir);
    fireEvent.keyDown(document, { key: "Escape" });
    expect(onCerrar).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.activeElement).toBe(abrir);
  });

  it("el botón «Cerrar» cierra y devuelve el foco", () => {
    render(<Arnes />);
    const abrir = screen.getByRole("button", { name: "Ver dictamen" });
    abrir.focus();
    fireEvent.click(abrir);
    fireEvent.click(screen.getByRole("button", { name: "Cerrar" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.activeElement).toBe(abrir);
  });

  it("ocupado: Escape no cierra", () => {
    const onCerrar = vi.fn();
    render(<PanelLateral abierto ocupado onCerrar={onCerrar} titulo="T">contenido</PanelLateral>);
    fireEvent.keyDown(document, { key: "Escape" });
    expect(onCerrar).not.toHaveBeenCalled();
  });
});
