import { fireEvent, render, screen } from "@testing-library/react";
import { Info } from "lucide-react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DrFlit } from "@/components/chat/DrFlit";
import { ModalFlit } from "@/components/modales/ModalFlit";

// Contrato responsive §1 (S4): en iOS la página de atrás no se desplaza mientras hay un modal o el panel del chat abierto.
// El bloqueo fija `body` (lo que sí funciona en WebKit) y lo suelta al cerrar, sin tocar foco, Escape ni el clic afuera.
afterEach(() => {
  document.body.removeAttribute("style");
});

describe("bloqueo de scroll en iOS", () => {
  it("ModalFlit fija la página mientras está abierto y la suelta al cerrar", () => {
    const scrollTo = vi.spyOn(window, "scrollTo").mockImplementation(() => undefined);
    const onCerrar = vi.fn();
    const { rerender } = render(<ModalFlit abierto onCerrar={onCerrar} titulo="Requisitos" icono={Info}><p>contenido</p></ModalFlit>);

    expect(document.body.style.position).toBe("fixed");
    expect(document.body.style.overflow).toBe("hidden");

    // El cierre lo sigue mandando el estado: Escape pide cerrar y no deja de hacerlo por el bloqueo.
    fireEvent(screen.getByRole("dialog", { hidden: true }), new Event("cancel", { cancelable: true }));
    expect(onCerrar).toHaveBeenCalledTimes(1);

    rerender(<ModalFlit abierto={false} onCerrar={onCerrar} titulo="Requisitos" icono={Info}><p>contenido</p></ModalFlit>);
    expect(document.body.style.position).toBe("");
    expect(document.body.style.overflow).toBe("");
    expect(scrollTo).toHaveBeenCalled();
    scrollTo.mockRestore();
  });

  it("el panel de Dr. FLIT fija la página al abrir y la suelta al minimizar, con el foco de vuelta en el lanzador", () => {
    vi.spyOn(window, "scrollTo").mockImplementation(() => undefined);
    render(<DrFlit />);
    const lanzador = screen.getByRole("button", { name: /Abrir Dr\. FLIT/ });
    expect(document.body.style.position).toBe("");

    fireEvent.click(lanzador);
    expect(document.body.style.position).toBe("fixed");

    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });
    expect(document.body.style.position).toBe("");
    expect(lanzador).toHaveFocus();
    vi.restoreAllMocks();
  });
});
