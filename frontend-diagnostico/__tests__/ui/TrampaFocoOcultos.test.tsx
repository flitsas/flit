import { useRef, useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { Modal } from "@flit/ui/Modal";
import { PanelLateral } from "@flit/ui/PanelLateral";
import { enfocablesDe, useTrampaFoco } from "@flit/ui/useTrampaFoco";

// Uso de ejemplo: useTrampaFoco({ activa: abierto, contenedor: panelRef, onEscape: cerrar }) dentro de una capa modal.
// HU #14562 · N1 (reauditoría de UI): un control con display:none o visibility:hidden (propio o de un ancestro) no entra
// en la trampa, y si el destino del foco inicial no lo acepta, el foco cae en el panel y no se pierde en body.
// Observación 7 del code review: con dos capas montadas en el mismo render manda la más anidada.

const tab = (shiftKey = false) => fireEvent.keyDown(document.activeElement ?? document.body, { key: "Tab", shiftKey });

describe("useTrampaFoco: elementos ocultos por estilo (HU #14562 · N1)", () => {
  it("N1: un input file oculto (display:none) como primer campo no recibe el foco inicial; va al título", () => {
    render(
      <Modal open onClose={() => {}} title="Probar la versión 3">
        <input type="file" data-testid="archivo" style={{ display: "none" }} />
        <button type="button">Elegir archivo de muestra</button>
      </Modal>,
    );
    const dialogo = screen.getByRole("dialog");
    expect(screen.getByTestId("archivo")).not.toHaveFocus();
    expect(dialogo.contains(document.activeElement)).toBe(true);
    expect(screen.getByRole("heading", { name: "Probar la versión 3" })).toHaveFocus();
  });

  it("N1: Mayús+Tab desde el primero salta el último control si está oculto", () => {
    render(
      <Modal open onClose={() => {}} title="Probar">
        <label>
          Motivo
          <input />
        </label>
        <button type="button">Visible</button>
        <button type="button" style={{ display: "none" }}>
          Oculto al final
        </button>
      </Modal>,
    );
    const lista = enfocablesDe(screen.getByRole("dialog"));
    expect(lista.map((el) => el.textContent || el.tagName)).not.toContain("Oculto al final");
    const ultimoVisible = lista[lista.length - 1];
    lista[0].focus();
    tab(true);
    expect(ultimoVisible).toHaveFocus();
    expect(screen.getByText("Oculto al final")).not.toHaveFocus();
  });

  it("N1: tampoco cuentan los controles dentro de un ancestro con display:none o visibility:hidden", () => {
    render(
      <Modal open onClose={() => {}} title="Capa">
        <div style={{ display: "none" }}>
          <button type="button">En bloque oculto</button>
        </div>
        <div style={{ visibility: "hidden" }}>
          <button type="button">En bloque invisible</button>
        </div>
        <button type="button" style={{ visibility: "hidden" }}>
          Invisible
        </button>
        <button type="button">Único visible</button>
      </Modal>,
    );
    const nombres = enfocablesDe(screen.getByRole("dialog")).map((el) => el.textContent);
    expect(nombres).not.toContain("En bloque oculto");
    expect(nombres).not.toContain("En bloque invisible");
    expect(nombres).not.toContain("Invisible");
    expect(nombres).toContain("Único visible");
  });

  it("N1: si el destino del foco inicial no lo acepta, el foco cae en el panel y no en body", () => {
    function Capa() {
      const ref = useRef<HTMLDivElement>(null);
      const destino = useRef<HTMLSpanElement>(null);
      // Un <span> sin tabindex: focus() no hace nada.
      useTrampaFoco({ activa: true, contenedor: ref, focoInicial: () => destino.current });
      return (
        <div ref={ref} tabIndex={-1} data-testid="panel">
          <span ref={destino}>Sin foco</span>
        </div>
      );
    }
    render(<Capa />);
    expect(screen.getByTestId("panel")).toHaveFocus();
  });
});

describe("useTrampaFoco: orden de la pila con capas del mismo render (code review, observación 7)", () => {
  it("un Modal anidado que se monta en el mismo render que su PanelLateral queda arriba: Escape cierra solo el Modal", () => {
    const cerrarPanel = vi.fn();
    function Capas() {
      const [modal, setModal] = useState(true);
      return (
        <PanelLateral abierto onCerrar={cerrarPanel} titulo="Dictamen">
          <button type="button">Fondo del panel</button>
          <Modal open={modal} onClose={() => setModal(false)} title="Crear trámite">
            <label>
              Tipo
              <input />
            </label>
          </Modal>
        </PanelLateral>
      );
    }
    render(<Capas />);
    fireEvent.keyDown(document.activeElement ?? document.body, { key: "Escape" });
    expect(screen.queryByRole("dialog", { name: "Crear trámite" })).toBeNull();
    expect(cerrarPanel).not.toHaveBeenCalled();
    fireEvent.keyDown(document.activeElement ?? document.body, { key: "Escape" });
    expect(cerrarPanel).toHaveBeenCalledTimes(1);
  });
});
