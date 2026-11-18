import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
import { useRef, useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { PanelModal } from "@flit/ui/PanelModal";

// Uso de ejemplo: <PanelModal abierto={!!vehiculo} onCerrar={cerrar} titulo="Dictamen · ABC123" cerrarConVelo={false}
//   acciones={<Bandera />} pie={<Button>Crear trámite</Button>}>…</PanelModal>
// Ola Diagnóstico UX 2026-11-15 · FE-1 (spec-ui §1.4–§1.8): el antiguo PanelLateral de 640 px pasa al Modal de la suite
// con `size="panel"`, las mismas props (sin `ancho`) y la misma distribución: título y descripción · acciones · Cerrar ·
// cuerpo · pie.

function Arnes({
  onCerrar = () => {},
  cuerpo = <a href="#x">Detalle</a>,
  cerrarConVelo,
  ocupado,
}: {
  onCerrar?: () => void;
  cuerpo?: React.ReactNode;
  cerrarConVelo?: boolean;
  ocupado?: boolean;
}) {
  const [abierto, setAbierto] = useState(false);
  return (
    <>
      <button type="button" onClick={() => setAbierto(true)}>
        Ver dictamen
      </button>
      <PanelModal
        abierto={abierto}
        onCerrar={() => {
          onCerrar();
          setAbierto(false);
        }}
        titulo="ABC123 · Dictamen del vehículo"
        descripcion="Lote Demo"
        acciones={<button type="button">Bandera</button>}
        pie={<button type="button">Crear trámite</button>}
        cerrarConVelo={cerrarConVelo}
        ocupado={ocupado}
      >
        {cuerpo}
      </PanelModal>
    </>
  );
}

const abrir = () => {
  const boton = screen.getByRole("button", { name: "Ver dictamen" });
  boton.focus();
  fireEvent.click(boton);
  return boton;
};

describe("PanelModal (FE-1, spec-ui §1.4)", () => {
  it("cerrado no pinta nada", () => {
    render(<PanelModal abierto={false} onCerrar={vi.fn()} titulo="X">contenido</PanelModal>);
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("abierto: role=dialog con aria-modal, nombre por aria-labelledby y panel de max-w-[640px]", () => {
    render(<Arnes />);
    abrir();
    const dialogo = screen.getByRole("dialog", { name: "ABC123 · Dictamen del vehículo" });
    expect(dialogo).toHaveAttribute("aria-modal", "true");
    const titulo = screen.getByRole("heading", { level: 2, name: "ABC123 · Dictamen del vehículo" });
    expect(dialogo).toHaveAttribute("aria-labelledby", titulo.id);
    expect(dialogo.firstElementChild?.className).toContain("max-w-[640px]");
  });

  it("misma distribución que el panel: título y descripción · acciones · Cerrar · cuerpo · pie", () => {
    render(<Arnes />);
    abrir();
    const dialogo = screen.getByRole("dialog");
    const orden = [
      screen.getByRole("heading", { name: "ABC123 · Dictamen del vehículo" }),
      screen.getByText("Lote Demo"),
      screen.getByRole("button", { name: "Bandera" }),
      screen.getByRole("button", { name: "Cerrar" }),
      screen.getByRole("link", { name: "Detalle" }),
      screen.getByRole("button", { name: "Crear trámite" }),
    ];
    for (let i = 1; i < orden.length; i++) {
      expect(orden[i - 1].compareDocumentPosition(orden[i]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    }
    expect(dialogo.contains(orden.at(-1)!)).toBe(true);
    // «Cerrar» conserva el aspecto del panel (texto + subrayado), no la X de la cabecera centrada del Modal.
    expect(screen.getByRole("button", { name: "Cerrar" })).toHaveTextContent("Cerrar");
  });

  it("sin campos en el cuerpo, el foco inicial va al título (tabIndex=-1)", () => {
    render(<Arnes />);
    abrir();
    const titulo = screen.getByRole("heading", { name: "ABC123 · Dictamen del vehículo" });
    expect(titulo).toHaveAttribute("tabindex", "-1");
    expect(titulo).toHaveFocus();
  });

  it("con un campo en el cuerpo, el foco inicial va al primer campo", () => {
    render(<Arnes cuerpo={<label>Observación<textarea /></label>} />);
    abrir();
    expect(screen.getByRole("textbox", { name: "Observación" })).toHaveFocus();
  });

  it("Escape cierra y el foco vuelve al disparador", () => {
    const onCerrar = vi.fn();
    render(<Arnes onCerrar={onCerrar} />);
    const boton = abrir();
    fireEvent.keyDown(document.activeElement!, { key: "Escape" });
    expect(onCerrar).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(boton).toHaveFocus();
  });

  it("«Cerrar» cierra y devuelve el foco", () => {
    render(<Arnes />);
    const boton = abrir();
    fireEvent.click(screen.getByRole("button", { name: "Cerrar" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(boton).toHaveFocus();
  });

  it("clic en el velo cierra por defecto (como el panel)", () => {
    const onCerrar = vi.fn();
    render(<Arnes onCerrar={onCerrar} />);
    abrir();
    fireEvent.mouseDown(screen.getByRole("dialog"));
    expect(onCerrar).toHaveBeenCalledTimes(1);
  });

  it("cerrarConVelo={false}: el clic en el velo no cierra; Escape y «Cerrar» sí", () => {
    const onCerrar = vi.fn();
    render(<Arnes onCerrar={onCerrar} cerrarConVelo={false} cuerpo={<label>Motivo<input /></label>} />);
    abrir();
    fireEvent.mouseDown(screen.getByRole("dialog"));
    expect(onCerrar).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    fireEvent.keyDown(document.activeElement!, { key: "Escape" });
    expect(onCerrar).toHaveBeenCalledTimes(1);
  });

  it("ocupado: ni Escape, ni el velo, ni «Cerrar» (aria-disabled) cierran", () => {
    const onCerrar = vi.fn();
    render(<PanelModal abierto ocupado onCerrar={onCerrar} titulo="T">contenido</PanelModal>);
    fireEvent.keyDown(document.activeElement ?? document.body, { key: "Escape" });
    fireEvent.mouseDown(screen.getByRole("dialog"));
    const cerrar = screen.getByRole("button", { name: "Cerrar" });
    expect(cerrar).toHaveAttribute("aria-disabled", "true");
    fireEvent.click(cerrar);
    expect(onCerrar).not.toHaveBeenCalled();
  });

  it("aparece por opacidad, sin escalas, y respeta menos movimiento", () => {
    render(<PanelModal abierto onCerrar={vi.fn()} titulo="T">contenido</PanelModal>);
    const panel = screen.getByRole("dialog").firstElementChild as HTMLElement;
    expect(panel.className).toContain("transition-opacity");
    expect(panel.className).toContain("motion-reduce:transition-none");
    expect(panel.className).not.toMatch(/scale-/);
  });

  it("DetalleCelda.tsx:57: closest('[role=\"dialog\"]') desde el contenido resuelve el modal que lo contiene", () => {
    let ancla: HTMLElement | null = null;
    function Contenido() {
      const ref = useRef<HTMLSpanElement>(null);
      return (
        <span
          ref={(el) => {
            ref.current = el;
            ancla = el;
          }}
        >
          Celda
        </span>
      );
    }
    render(<PanelModal abierto onCerrar={vi.fn()} titulo="Dictamen"><Contenido /></PanelModal>);
    expect(ancla).not.toBeNull();
    expect(ancla!.closest('[role="dialog"]')).toBe(screen.getByRole("dialog", { name: "Dictamen" }));
  });
});

describe("Diagnóstico ya no usa PanelLateral (FE-1)", () => {
  const raiz = path.resolve(__dirname, "../../components");
  const archivos = (dir: string): string[] =>
    readdirSync(dir).flatMap((n) => {
      const p = path.join(dir, n);
      if (statSync(p).isDirectory()) return n === "__tests__" ? [] : archivos(p);
      return /\.tsx?$/.test(n) && !/\.test\.tsx?$/.test(n) ? [p] : [];
    });

  it("ningún componente importa PanelLateral ni le pasa `ancho`", () => {
    const conPanel = archivos(raiz).filter((a) => /PanelLateral|ancho=\{640\}/.test(readFileSync(a, "utf8")));
    expect(conPanel.map((a) => path.relative(raiz, a))).toEqual([]);
  });
});
