import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { Tooltip } from "@flit/ui/Tooltip";
import type { ComponentProps } from "react";

// Uso de ejemplo: <Tooltip contenido={<>Ejecutado<span className={TOOLTIP_SECUNDARIO_CLS}>Dato del 15/11/2026 00:04</span></>}>
//   <a href={urlDetalle}>Ejecutado</a>
// </Tooltip>
// HU #14584 (spec-ui §5): tooltip compartido del kit. Hover con 300 ms, foco inmediato, Esc sin mover el foco, gracia de
// 100 ms con puente a la burbuja (WCAG 1.4.13), flip arriba/abajo, toggletip táctil y `aria-describedby` concatenado.
// SP-19: por defecto el contenido se monta al abrir (nada pre-renderizado oculto); `persistente` solo para literales fijos.

const ALTO_BURBUJA = 40;
const ANCHO_BURBUJA = 120;

/** jsdom no hace layout: el disparador mide lo que diga `rectDisparador` y la burbuja 120 × 40. */
let rectDisparador = { top: 100, bottom: 132, left: 200, right: 280, width: 80, height: 32 };
beforeEach(() => {
  rectDisparador = { top: 100, bottom: 132, left: 200, right: 280, width: 80, height: 32 };
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (this: HTMLElement) {
    const r =
      this.getAttribute("role") === "tooltip"
        ? { top: 0, bottom: ALTO_BURBUJA, left: 0, right: ANCHO_BURBUJA, width: ANCHO_BURBUJA, height: ALTO_BURBUJA }
        : this.dataset.prueba === "disparador"
          ? rectDisparador
          : { top: 0, bottom: 0, left: 0, right: 0, width: 0, height: 0 };
    return { ...r, x: r.left, y: r.top, toJSON: () => r } as DOMRect;
  });
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.useRealTimers();
});

function montar(props: Partial<Parameters<typeof Tooltip>[0]> = {}) {
  return render(
    <Tooltip contenido="Dato del 15/11/2026 00:04" {...props}>
      <button type="button" data-prueba="disparador">
        Ejecutado
      </button>
    </Tooltip>,
  );
}

const disparador = () => screen.getByRole("button", { name: "Ejecutado" });
const pointer = (tipo: "pointerEnter" | "pointerLeave" | "pointerDown", el: Element, pointerType = "mouse") =>
  fireEvent[tipo](el, { pointerType });

describe("Tooltip — apertura y cierre (HU #14584, spec-ui §5.2, A5.1)", () => {
  it("hover abre a los 300 ms, no antes", () => {
    vi.useFakeTimers();
    montar();
    pointer("pointerEnter", disparador());
    act(() => vi.advanceTimersByTime(299));
    expect(screen.queryByRole("tooltip")).toBeNull();
    act(() => vi.advanceTimersByTime(1));
    expect(screen.getByRole("tooltip")).toHaveTextContent("Dato del 15/11/2026 00:04");
  });

  it("el foco por teclado abre al instante y el blur cierra", () => {
    montar();
    act(() => disparador().focus());
    expect(screen.getByRole("tooltip")).toBeInTheDocument();
    act(() => disparador().blur());
    expect(screen.queryByRole("tooltip")).toBeNull();
  });

  it("Esc cierra la burbuja, el foco sigue en el disparador y el evento no sube si estaba abierta", () => {
    const alContenedor = vi.fn();
    render(
      <div onKeyDown={alContenedor}>
        <Tooltip contenido="Motivo">
          <button type="button" data-prueba="disparador">
            Ejecutado
          </button>
        </Tooltip>
      </div>,
    );
    act(() => disparador().focus());
    fireEvent.keyDown(disparador(), { key: "Escape" });
    expect(screen.queryByRole("tooltip")).toBeNull();
    expect(disparador()).toHaveFocus();
    expect(alContenedor).not.toHaveBeenCalled();
    // Ya cerrada, Esc sí sube (cierra el modal o el menú contenedor).
    fireEvent.keyDown(disparador(), { key: "Escape" });
    expect(alContenedor).toHaveBeenCalledTimes(1);
  });

  it("al salir el puntero cierra tras 100 ms de gracia, y si pasa a la burbuja no se cierra (1.4.13)", () => {
    vi.useFakeTimers();
    montar();
    pointer("pointerEnter", disparador());
    act(() => vi.advanceTimersByTime(300));
    pointer("pointerLeave", disparador());
    act(() => vi.advanceTimersByTime(50));
    pointer("pointerEnter", screen.getByRole("tooltip"));
    act(() => vi.advanceTimersByTime(500));
    expect(screen.getByRole("tooltip")).toBeInTheDocument();
    pointer("pointerLeave", screen.getByRole("tooltip"));
    act(() => vi.advanceTimersByTime(100));
    expect(screen.queryByRole("tooltip")).toBeNull();
  });

  it("no se cierra solo por tiempo mientras el puntero sigue encima", () => {
    vi.useFakeTimers();
    montar();
    pointer("pointerEnter", disparador());
    act(() => vi.advanceTimersByTime(10_000));
    expect(screen.getByRole("tooltip")).toBeInTheDocument();
  });

  it("desactivado no abre ni con foco ni con hover", () => {
    vi.useFakeTimers();
    montar({ desactivado: true });
    act(() => disparador().focus());
    pointer("pointerEnter", disparador());
    act(() => vi.advanceTimersByTime(500));
    expect(screen.queryByRole("tooltip")).toBeNull();
  });
});

describe("Tooltip — accesibilidad y SP-19 (A5.2)", () => {
  it("abierta, aria-describedby del disparador contiene el id del role=tooltip y conserva los ids previos", () => {
    render(
      <>
        <span id="previo">Fuente RUNT</span>
        <Tooltip contenido="Motivo">
          <button type="button" data-prueba="disparador" aria-describedby="previo">
            Ejecutado
          </button>
        </Tooltip>
      </>,
    );
    act(() => disparador().focus());
    const burbuja = screen.getByRole("tooltip");
    const ids = (disparador().getAttribute("aria-describedby") ?? "").split(" ");
    expect(ids).toContain("previo");
    expect(ids).toContain(burbuja.id);
    expect(disparador()).toHaveAccessibleDescription("Fuente RUNT Motivo");
    expect(burbuja).not.toHaveAttribute("aria-hidden");
  });

  it("SP-19: cerrada y sin `persistente`, el contenido no está en el DOM ni el disparador apunta a un id inexistente", () => {
    const { baseElement } = render(
      <Tooltip contenido="Texto del tooltip">
        <button type="button" data-prueba="disparador" aria-describedby="previo">
          Ejecutado
        </button>
      </Tooltip>,
    );
    expect(baseElement.innerHTML).not.toContain("Texto del tooltip");
    expect(disparador()).toHaveAttribute("aria-describedby", "previo");
  });

  it("persistente: la burbuja queda en el DOM cerrada (sin aria-hidden) y sigue describiendo al disparador", () => {
    montar({ persistente: true, contenido: "El RUNT vehículo siempre se consulta." });
    const burbuja = screen.getByRole("tooltip");
    expect(burbuja).toHaveAttribute("data-abierto", "false");
    expect(burbuja.className).toContain("opacity-0");
    expect(burbuja).not.toHaveAttribute("aria-hidden");
    expect(disparador()).toHaveAccessibleDescription("El RUNT vehículo siempre se consulta.");
  });

  it("un hijo no enfocable se envuelve en un span con tabIndex=0", () => {
    render(
      <Tooltip contenido="Sin solicitar · No se pidió en este lote">
        <span>Sin solicitar</span>
      </Tooltip>,
    );
    const envoltorio = screen.getByText("Sin solicitar").parentElement!;
    expect(envoltorio.tagName).toBe("SPAN");
    expect(envoltorio).toHaveAttribute("tabindex", "0");
  });

  it("aspecto del kit: radio 9 px, borde y superficie por token, sin sombra, máx. 18rem y z-[60]", () => {
    montar();
    act(() => disparador().focus());
    const clases = screen.getByRole("tooltip").className;
    for (const c of ["rounded-[9px]", "border-[color:var(--di-border)]", "bg-[var(--di-surface-elevated)]", "max-w-[18rem]", "z-[60]", "text-[color:var(--di-action)]"])
      expect(clases).toContain(c);
    expect(clases).not.toMatch(/shadow-/);
    expect(clases).not.toMatch(/#[0-9a-f]{3,6}/i);
  });
});

describe("Tooltip — posición con flip (A5.3)", () => {
  it("con espacio abajo, va abajo (preferida)", () => {
    montar();
    act(() => disparador().focus());
    const burbuja = screen.getByRole("tooltip");
    expect(burbuja).toHaveAttribute("data-lado", "abajo");
    expect(parseFloat(burbuja.style.top)).toBeGreaterThanOrEqual(rectDisparador.bottom);
  });

  it("pegado al borde inferior, voltea arriba", () => {
    rectDisparador = { top: window.innerHeight - 30, bottom: window.innerHeight - 2, left: 200, right: 280, width: 80, height: 28 };
    montar();
    act(() => disparador().focus());
    const burbuja = screen.getByRole("tooltip");
    expect(burbuja).toHaveAttribute("data-lado", "arriba");
    expect(parseFloat(burbuja.style.top) + ALTO_BURBUJA).toBeLessThanOrEqual(rectDisparador.top);
  });

  it("lado=arriba sin espacio arriba voltea abajo", () => {
    rectDisparador = { top: 4, bottom: 36, left: 200, right: 280, width: 80, height: 32 };
    montar({ lado: "arriba" });
    act(() => disparador().focus());
    expect(screen.getByRole("tooltip")).toHaveAttribute("data-lado", "abajo");
  });

  it("se desplaza en horizontal para quedar a ≥ 8 px del borde", () => {
    rectDisparador = { top: 100, bottom: 132, left: 0, right: 20, width: 20, height: 32 };
    montar();
    act(() => disparador().focus());
    expect(parseFloat(screen.getByRole("tooltip").style.left)).toBeGreaterThanOrEqual(8);
  });
});

describe("Tooltip — táctil (spec-ui §5.2)", () => {
  it("un toque alterna (toggletip) y un toque fuera la cierra", () => {
    montar();
    pointer("pointerDown", disparador(), "touch");
    fireEvent.click(disparador());
    expect(screen.getByRole("tooltip")).toBeInTheDocument();
    pointer("pointerDown", document.body, "touch");
    expect(screen.queryByRole("tooltip")).toBeNull();
    pointer("pointerDown", disparador(), "touch");
    fireEvent.click(disparador());
    pointer("pointerDown", disparador(), "touch");
    fireEvent.click(disparador());
    expect(screen.queryByRole("tooltip")).toBeNull();
  });

  it("si el disparador es un enlace, el toque navega: no se intercepta ni se abre la burbuja", () => {
    render(
      <Tooltip contenido="Ejecutado · Dato del día">
        <a href="/detalle" data-prueba="disparador">
          Ejecutado
        </a>
      </Tooltip>,
    );
    const enlace = screen.getByRole("link", { name: "Ejecutado" });
    pointer("pointerDown", enlace, "touch");
    const evento = new MouseEvent("click", { bubbles: true, cancelable: true });
    enlace.dispatchEvent(evento);
    expect(evento.defaultPrevented).toBe(false);
    expect(screen.queryByRole("tooltip")).toBeNull();
  });

  it("el hover simulado de un toque no programa la apertura diferida", () => {
    vi.useFakeTimers();
    montar();
    pointer("pointerEnter", disparador(), "touch");
    act(() => vi.advanceTimersByTime(500));
    expect(screen.queryByRole("tooltip")).toBeNull();
  });
});

// Code review del kit (CR-3 y CR-4, 2026-11-16).
describe("Tooltip — envoltorio, componentes y Esc en captura (CR-3, CR-4)", () => {
  it("CR-3 (i): en el envoltorio, el onClick, onKeyDown y onFocus del hijo corren una sola vez", () => {
    const onClick = vi.fn();
    const onKeyDown = vi.fn();
    const onFocus = vi.fn();
    render(
      <Tooltip contenido="Sin solicitar">
        <span tabIndex={undefined} onClick={onClick} onKeyDown={onKeyDown} onFocus={onFocus}>
          Sin solicitar
        </span>
      </Tooltip>,
    );
    const hijo = screen.getByText("Sin solicitar");
    expect(hijo.parentElement).toHaveAttribute("tabindex", "0");
    fireEvent.click(hijo);
    fireEvent.keyDown(hijo, { key: "a" });
    fireEvent.focus(hijo);
    expect(onClick).toHaveBeenCalledTimes(1);
    expect(onKeyDown).toHaveBeenCalledTimes(1);
    expect(onFocus).toHaveBeenCalledTimes(1);
  });

  it("CR-3 (ii): un componente (no host) se clona sin envoltorio: una sola parada de Tab y su onClick una vez", () => {
    const onClick = vi.fn();
    function Boton(props: ComponentProps<"button">) {
      return <button type="button" data-prueba="disparador" {...props} />;
    }
    const { container } = render(
      <Tooltip contenido="Motivo">
        <Boton onClick={onClick}>Ejecutado</Boton>
      </Tooltip>,
    );
    expect(container.querySelectorAll("[tabindex='0']")).toHaveLength(0);
    expect(disparador().parentElement).toBe(container);
    fireEvent.click(disparador());
    expect(onClick).toHaveBeenCalledTimes(1);
    act(() => disparador().focus());
    expect(disparador().getAttribute("aria-describedby")).toBe(screen.getByRole("tooltip").id);
  });

  it("CR-4: con la burbuja abierta, Esc la cierra sin llegar al listener de document del modal; cerrada, sí llega", () => {
    const delModal = vi.fn();
    document.addEventListener("keydown", delModal);
    try {
      montar();
      act(() => disparador().focus());
      expect(screen.getByRole("tooltip")).toBeInTheDocument();
      fireEvent.keyDown(disparador(), { key: "Escape" });
      expect(screen.queryByRole("tooltip")).toBeNull();
      expect(delModal).not.toHaveBeenCalled();
      fireEvent.keyDown(disparador(), { key: "Escape" });
      expect(delModal).toHaveBeenCalledTimes(1);
    } finally {
      document.removeEventListener("keydown", delModal);
    }
  });

  it("CR-4: abierta por hover (foco fuera), Esc en otro elemento también se corta en captura", () => {
    vi.useFakeTimers();
    const delModal = vi.fn();
    document.addEventListener("keydown", delModal);
    try {
      montar();
      pointer("pointerEnter", disparador());
      act(() => vi.advanceTimersByTime(300));
      fireEvent.keyDown(document.body, { key: "Escape" });
      expect(screen.queryByRole("tooltip")).toBeNull();
      expect(delModal).not.toHaveBeenCalled();
    } finally {
      document.removeEventListener("keydown", delModal);
    }
  });
});
