import { useEffect, useRef, useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { Modal } from "@flit/ui/Modal";
import { PanelLateral } from "@flit/ui/PanelLateral";

// Uso de ejemplo: <Modal open={abierto} onClose={cerrar} title="¿Cerrar el lote?" footer={<Button>Cerrar lote</Button>}>
//   Ya no se podrán agregar vehículos.</Modal>
// HU #14562 · M2 (auditoría de la oleada): foco inicial, trampa con Tab y Mayús+Tab, devolución del foco al disparador
// y Escape solo para la capa de arriba (pila de capas compartida con PanelLateral, m-6).

function Arnes({ cuerpo, footer, busy = false }: { cuerpo: React.ReactNode; footer?: React.ReactNode; busy?: boolean }) {
  const [abierto, setAbierto] = useState(false);
  return (
    <>
      <button type="button" onClick={() => setAbierto(true)}>
        Abrir
      </button>
      <button type="button">Fondo</button>
      <Modal open={abierto} onClose={() => setAbierto(false)} title="¿Cerrar el lote?" footer={footer} busy={busy}>
        {cuerpo}
      </Modal>
    </>
  );
}

const tab = (shiftKey = false) => fireEvent.keyDown(document.activeElement ?? document.body, { key: "Tab", shiftKey });

describe("Modal: gestión de foco (HU #14562 · M2)", () => {
  it("M2: con un campo en el cuerpo, el foco inicial va al primer campo", () => {
    render(<Arnes cuerpo={<><p>Texto</p><label>Motivo<input /></label></>} footer={<button type="button">Aplicar</button>} />);
    fireEvent.click(screen.getByRole("button", { name: "Abrir" }));
    expect(screen.getByRole("textbox", { name: "Motivo" })).toHaveFocus();
  });

  it("M2: sin campos (confirmación), el foco inicial va al título, no a la acción destructiva", () => {
    render(<Arnes cuerpo={<p>Ya no se podrán agregar vehículos.</p>} footer={<button type="button">Cerrar lote</button>} />);
    fireEvent.click(screen.getByRole("button", { name: "Abrir" }));
    const titulo = screen.getByRole("heading", { name: "¿Cerrar el lote?" });
    expect(titulo).toHaveFocus();
    expect(titulo).toHaveAttribute("tabindex", "-1");
  });

  it("M2: [data-autofocus] manda sobre la regla por defecto", () => {
    render(
      <Arnes
        cuerpo={<label>Motivo<input /></label>}
        footer={<button type="button" data-autofocus>Cancelar</button>}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Abrir" }));
    expect(screen.getByRole("button", { name: "Cancelar" })).toHaveFocus();
  });

  it("M2 retrocompatible: si un hijo ya puso el foco en su montaje, el Modal no se lo quita", () => {
    function HijoQueEnfoca() {
      const ref = useRef<HTMLButtonElement>(null);
      useEffect(() => ref.current?.focus(), []);
      return (
        <>
          <input aria-label="Primero" />
          <button ref={ref} type="button">Mío</button>
        </>
      );
    }
    render(<Arnes cuerpo={<HijoQueEnfoca />} />);
    fireEvent.click(screen.getByRole("button", { name: "Abrir" }));
    expect(screen.getByRole("button", { name: "Mío" })).toHaveFocus();
  });

  it("M2: Tab desde el último control vuelve al primero y Mayús+Tab desde el primero va al último", () => {
    render(<Arnes cuerpo={<label>Motivo<input /></label>} footer={<button type="button">Aplicar</button>} />);
    fireEvent.click(screen.getByRole("button", { name: "Abrir" }));
    const cerrar = screen.getByRole("button", { name: "Cerrar" });
    const aplicar = screen.getByRole("button", { name: "Aplicar" });
    aplicar.focus();
    tab();
    expect(cerrar).toHaveFocus();
    tab(true);
    expect(aplicar).toHaveFocus();
  });

  it("M2: Mayús+Tab desde el título (antes del primer control) no se escapa al fondo", () => {
    render(<Arnes cuerpo={<p>Confirmación</p>} footer={<button type="button">Cerrar lote</button>} />);
    fireEvent.click(screen.getByRole("button", { name: "Abrir" }));
    expect(screen.getByRole("heading", { name: "¿Cerrar el lote?" })).toHaveFocus();
    tab(true);
    expect(screen.getByRole("button", { name: "Cerrar lote" })).toHaveFocus();
  });

  it("M2: si el foco quedó fuera del diálogo, Tab lo regresa adentro", () => {
    render(<Arnes cuerpo={<label>Motivo<input /></label>} />);
    fireEvent.click(screen.getByRole("button", { name: "Abrir" }));
    screen.getByRole("button", { name: "Fondo", hidden: true }).focus();
    tab();
    expect(screen.getByRole("button", { name: "Cerrar" })).toHaveFocus();
  });

  it("M2: al cerrar (X o Escape) el foco vuelve al disparador", () => {
    render(<Arnes cuerpo={<label>Motivo<input /></label>} />);
    const abrir = screen.getByRole("button", { name: "Abrir" });
    abrir.focus();
    fireEvent.click(abrir);
    fireEvent.click(screen.getByRole("button", { name: "Cerrar" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(abrir).toHaveFocus();

    fireEvent.click(abrir);
    fireEvent.keyDown(document.activeElement!, { key: "Escape" });
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(abrir).toHaveFocus();
  });

  it("M2 retrocompatible: si quien cierra mueve el foco a otro lado, el Modal no lo pisa", () => {
    function ArnesPropio() {
      const [abierto, setAbierto] = useState(true);
      const destino = useRef<HTMLButtonElement>(null);
      return (
        <>
          <button ref={destino} type="button">Destino</button>
          <Modal
            open={abierto}
            onClose={() => {
              destino.current?.focus();
              setAbierto(false);
            }}
            title="Título"
          >
            <input aria-label="Campo" />
          </Modal>
        </>
      );
    }
    render(<ArnesPropio />);
    fireEvent.click(screen.getByRole("button", { name: "Cerrar" }));
    expect(screen.getByRole("button", { name: "Destino" })).toHaveFocus();
  });

  it("M2: con header propio cuyo título no es enfocable y sin campos, el foco queda en el panel del diálogo", () => {
    render(
      <Modal open onClose={vi.fn()} title="Propio" header={({ titleId }) => <h3 id={titleId}>Propio</h3>}>
        <p>Solo texto</p>
      </Modal>,
    );
    expect(screen.getByRole("dialog").firstElementChild).toHaveFocus();
  });

  it("M2: ocupado (busy), Escape no cierra pero el foco sigue atrapado", () => {
    render(<Arnes cuerpo={<label>Motivo<input /></label>} busy />);
    fireEvent.click(screen.getByRole("button", { name: "Abrir" }));
    fireEvent.keyDown(document.activeElement!, { key: "Escape" });
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("m-6: con un Modal abierto sobre un PanelLateral, Escape cierra solo el Modal y Tab no sale de él", () => {
    const cerrarPanel = vi.fn();
    function Capas() {
      const [modal, setModal] = useState(false);
      return (
        <PanelLateral abierto onCerrar={cerrarPanel} titulo="Dictamen">
          <button type="button" onClick={() => setModal(true)}>
            Crear trámite
          </button>
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
    const disparador = screen.getByRole("button", { name: "Crear trámite" });
    disparador.focus();
    fireEvent.click(disparador);
    const campo = screen.getByRole("textbox", { name: "Tipo" });
    expect(campo).toHaveFocus();
    tab();
    expect(screen.getAllByRole("button", { name: "Cerrar" }).at(-1)).toHaveFocus();
    tab(true);
    expect(campo).toHaveFocus();
    fireEvent.keyDown(document.activeElement!, { key: "Escape" });
    expect(screen.queryByRole("textbox", { name: "Tipo" })).toBeNull();
    expect(cerrarPanel).not.toHaveBeenCalled();
    expect(disparador).toHaveFocus();
    fireEvent.keyDown(document.activeElement!, { key: "Escape" });
    expect(cerrarPanel).toHaveBeenCalledTimes(1);
  });

  it("M2: los elementos ocultos, deshabilitados o con tabindex=-1 no cuentan en la trampa", () => {
    render(
      <Arnes
        cuerpo={
          <>
            <label>Motivo<input /></label>
            <details><summary>Ver más</summary></details>
            <div hidden><button type="button">Oculto</button></div>
            <button type="button" disabled>Inactivo</button>
            <button type="button" tabIndex={-1}>Fuera de Tab</button>
          </>
        }
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Abrir" }));
    screen.getByText("Ver más").focus();
    tab();
    expect(screen.getByRole("button", { name: "Cerrar" })).toHaveFocus();
    tab(true);
    expect(screen.getByText("Ver más")).toHaveFocus();
  });
});
