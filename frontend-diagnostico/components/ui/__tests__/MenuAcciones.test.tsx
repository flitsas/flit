import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { ActionsMenu, type ActionsMenuItem } from "@flit/ui";
import { MenuAcciones } from "@/components/ui/MenuAcciones";

// Uso de ejemplo: <MenuAcciones ariaLabel="Acciones de la placa ABC123" items={[
//   { key: "deuda", label: "Ver deuda", onSelect: ({ disparador }) => abrirDeuda(disparador) },
//   { key: "tramite", label: "Ver trámite #41", href: url, target: "_blank" },
//   { key: "pdf", label: "Descargar PDF", href: urlPdf, download: true, disabled: true, disabledReason: "Disponible cuando…" },
// ]} />
// Ola Diagnóstico UX 2026-11-15 · FE-2 (spec-ui §2.2) y U10-1: menú propio de Diagnóstico con el aspecto del Kit 2026,
// ítem enlace, `onSelect(ctx)` con el disparador, motivo de deshabilitado visible y anunciado, `hint`, `ocupado` y foco
// inicial. `@flit/ui/ActionsMenu` (Comparendos) volvió a su versión previa: sus casos siguen aquí como guarda.

const abrir = (nombre = "Acciones de la placa ABC123") => {
  const disparador = screen.getByRole("button", { name: nombre });
  fireEvent.click(disparador);
  return disparador;
};

describe("@flit/ui ActionsMenu — Comparendos sin cambios (versión previa a FE-2)", () => {
  const items: ActionsMenuItem[] = [
    { key: "ver", label: "Ver detalle", onSelect: vi.fn() },
    { key: "pago", label: "Registrar pago", onSelect: vi.fn(), disabled: true, disabledReason: "Ya tiene un pago registrado." },
  ];

  it("el ítem deshabilitado sigue con `disabled` nativo y el motivo en `title`; el aspecto es el anterior", () => {
    render(<ActionsMenu ariaLabel="Acciones de la placa ABC123" items={items} />);
    const disparador = abrir();
    expect(disparador.className).toContain("rounded-xl");
    const pago = screen.getByRole("menuitem", { name: "Registrar pago" });
    expect(pago).toBeDisabled();
    expect(pago).toHaveAttribute("title", "Ya tiene un pago registrado.");
    expect(screen.queryByText("Ya tiene un pago registrado.")).toBeNull();
    expect(screen.getByRole("menu").className).toContain("rounded-xl");
  });

  it("onSelect sin argumentos sigue funcionando y el foco vuelve al disparador", () => {
    const ver = vi.fn();
    render(<ActionsMenu ariaLabel="Acciones de la placa ABC123" items={[{ key: "ver", label: "Ver detalle", onSelect: ver }]} />);
    const disparador = abrir();
    fireEvent.click(screen.getByRole("menuitem", { name: "Ver detalle" }));
    expect(ver).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("menu")).toBeNull();
    expect(disparador).toHaveFocus();
  });
});

describe("MenuAcciones — Kit 2026 propio de Diagnóstico (FE-2, U10-1)", () => {
  it("aspecto del Kit 2026: píldora de 32 px y ≥ 44 px de ancho, menú de radio 9 px e ítems de 40 px", () => {
    render(<MenuAcciones ariaLabel="Acciones de la placa ABC123" items={[{ key: "a", label: "Ver deuda", onSelect: vi.fn() }]} />);
    const disparador = abrir();
    for (const c of ["min-h-8", "min-w-11", "rounded-full", "text-[color:var(--di-action)]"]) expect(disparador.className).toContain(c);
    expect(disparador).toHaveAttribute("aria-haspopup", "menu");
    expect(disparador).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("menu").className).toContain("rounded-[9px]");
    expect(screen.getByRole("menuitem", { name: "Ver deuda" }).className).toContain("min-h-10");
  });

  it("onSelect recibe el disparador para devolverle el foco al cerrar un modal", () => {
    const onSelect = vi.fn();
    render(<MenuAcciones ariaLabel="Acciones de la placa ABC123" items={[{ key: "a", label: "Ver deuda", onSelect }]} />);
    const disparador = abrir();
    fireEvent.click(screen.getByRole("menuitem", { name: "Ver deuda" }));
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect.mock.calls[0][0].disparador).toBe(disparador);
  });

  it("ítem enlace: <a role=menuitem> con href, target _blank y rel noopener; download para descargas", () => {
    let destino: EventTarget | null = null;
    const alDescargar = vi.fn((ctx) => {
      destino = ctx.evento.currentTarget;
      ctx.evento.preventDefault();
    });
    render(
      <MenuAcciones
        ariaLabel="Acciones de la placa ABC123"
        items={[
          { key: "t", label: "Ver trámite #41", ariaLabel: "Ver trámite #41 de la placa ABC123 (se abre en una pestaña nueva)", href: "https://tramites.example/t/41", target: "_blank" },
          { key: "pdf", label: "Descargar PDF", href: "/api/pdf", download: true, onSelect: alDescargar },
        ]}
      />,
    );
    abrir();
    const tramite = screen.getByRole("menuitem", { name: "Ver trámite #41 de la placa ABC123 (se abre en una pestaña nueva)" });
    expect(tramite.tagName).toBe("A");
    expect(tramite).toHaveAttribute("href", "https://tramites.example/t/41");
    expect(tramite).toHaveAttribute("target", "_blank");
    expect(tramite).toHaveAttribute("rel", "noopener noreferrer");
    const pdf = screen.getByRole("menuitem", { name: "Descargar PDF" });
    expect(pdf).toHaveAttribute("download");
    fireEvent.click(pdf);
    expect(alDescargar).toHaveBeenCalledTimes(1);
    expect(destino).toBe(pdf);
  });

  it("deshabilitado con motivo: aria-disabled (no disabled), motivo visible enlazado por aria-describedby, y no hace nada", () => {
    const onSelect = vi.fn();
    render(
      <MenuAcciones
        ariaLabel="Acciones de la placa ABC123"
        items={[{ key: "pdf", label: "Descargar PDF", onSelect, disabled: true, disabledReason: "Disponible cuando terminen las consultas" }]}
      />,
    );
    abrir();
    const pdf = screen.getByRole("menuitem", { name: "Descargar PDF" });
    expect(pdf).not.toBeDisabled();
    expect(pdf).toHaveAttribute("aria-disabled", "true");
    expect(pdf).toHaveAccessibleDescription("Disponible cuando terminen las consultas");
    const motivo = screen.getByText("Disponible cuando terminen las consultas");
    expect(motivo).toBeVisible();
    // El motivo conserva el contraste: sin opacidad.
    expect(motivo.className).not.toContain("opacity");
    expect(motivo.className).toContain("text-[color:var(--di-text-tertiary)]");
    fireEvent.click(pdf);
    expect(onSelect).not.toHaveBeenCalled();
    expect(screen.getByRole("menu")).toBeInTheDocument();
  });

  it("hint: una segunda línea visible también en ítems habilitados", () => {
    render(
      <MenuAcciones
        ariaLabel="Acciones de la placa ABC123"
        items={[{ key: "pdf", label: "Descargar PDF", onSelect: vi.fn(), hint: "Las consultas no disponibles aparecerán así en el PDF" }]}
      />,
    );
    abrir();
    expect(screen.getByRole("menuitem", { name: "Descargar PDF" })).toHaveAccessibleDescription("Las consultas no disponibles aparecerán así en el PDF");
  });

  it("teclado: ↓/↑/Home/End recorren también los ítems aria-disabled; Escape cierra y devuelve el foco", () => {
    render(
      <MenuAcciones
        ariaLabel="Acciones de la placa ABC123"
        items={[
          { key: "a", label: "Sanear", onSelect: vi.fn() },
          { key: "b", label: "Crear trámite", onSelect: vi.fn(), disabled: true, disabledReason: "Ya tiene un trámite" },
          { key: "c", label: "Ver deuda", onSelect: vi.fn() },
        ]}
      />,
    );
    const disparador = abrir();
    const [a, b, c] = screen.getAllByRole("menuitem");
    expect(a).toHaveFocus();
    fireEvent.keyDown(a, { key: "ArrowDown" });
    expect(b).toHaveFocus();
    fireEvent.keyDown(b, { key: "End" });
    expect(c).toHaveFocus();
    fireEvent.keyDown(c, { key: "ArrowDown" });
    expect(a).toHaveFocus();
    fireEvent.keyDown(a, { key: "ArrowUp" });
    expect(c).toHaveFocus();
    fireEvent.keyDown(c, { key: "Home" });
    expect(a).toHaveFocus();
    fireEvent.keyDown(a, { key: "Escape" });
    expect(screen.queryByRole("menu")).toBeNull();
    expect(disparador).toHaveFocus();
  });

  it("ocupado: el disparador lleva aria-busy", () => {
    render(<MenuAcciones ocupado ariaLabel="Acciones de la placa ABC123" items={[{ key: "a", label: "Creando…", onSelect: vi.fn(), disabled: true }]} />);
    expect(screen.getByRole("button", { name: "Acciones de la placa ABC123" })).toHaveAttribute("aria-busy", "true");
  });

  it("el menú se portaliza a body (no lo recorta el scroll horizontal de la grilla)", () => {
    render(
      <div className="overflow-x-auto" data-testid="scroll">
        <MenuAcciones ariaLabel="Acciones de la placa ABC123" items={[{ key: "a", label: "Ver deuda", onSelect: vi.fn() }]} />
      </div>,
    );
    abrir();
    expect(screen.getByTestId("scroll").contains(screen.getByRole("menu"))).toBe(false);
    expect(screen.getByRole("menu").parentElement).toBe(document.body);
  });
});

// HU #14584 (spec-ui §4, A4.1–A4.3): flip vertical. Abre hacia arriba si no cabe abajo; si no cabe en ninguno de los dos
// lados, va al de más espacio con altura máxima y scroll interno (último recurso). Separador entre grupos de ítems.
describe("MenuAcciones — flip vertical y grupos (HU #14584)", () => {
  const ALTO_MENU = 160;
  let rectBoton = { top: 100, bottom: 132, left: 600, right: 700, width: 100, height: 32 };
  let altoMenu = ALTO_MENU;

  beforeEach(() => {
    rectBoton = { top: 100, bottom: 132, left: 600, right: 700, width: 100, height: 32 };
    altoMenu = ALTO_MENU;
    vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (this: HTMLElement) {
      const r =
        this.getAttribute("role") === "menu"
          ? { top: 0, bottom: altoMenu, left: 0, right: 240, width: 240, height: altoMenu }
          : this.getAttribute("aria-haspopup") === "menu"
            ? rectBoton
            : { top: 0, bottom: 0, left: 0, right: 0, width: 0, height: 0 };
      return { ...r, x: r.left, y: r.top, toJSON: () => r } as DOMRect;
    });
  });
  afterEach(() => vi.restoreAllMocks());

  const items = [
    { key: "a", label: "Ver dictamen", onSelect: vi.fn() },
    { key: "b", label: "Ver deuda", onSelect: vi.fn() },
  ];

  it("A4.2: con espacio abajo abre hacia abajo (data-lado=abajo), sin altura máxima", () => {
    render(<MenuAcciones ariaLabel="Acciones de la placa ABC123" items={items} />);
    abrir();
    const menu = screen.getByRole("menu");
    expect(menu).toHaveAttribute("data-lado", "abajo");
    expect(parseFloat(menu.style.top)).toBe(rectBoton.bottom + 4);
    expect(menu.style.maxHeight).toBe("");
    expect(menu.style.visibility).not.toBe("hidden");
  });

  it("A4.1: a 40 px del borde inferior abre hacia arriba con todos los ítems visibles y sin scroll", () => {
    rectBoton = { top: window.innerHeight - 72, bottom: window.innerHeight - 40, left: 600, right: 700, width: 100, height: 32 };
    render(<MenuAcciones ariaLabel="Acciones de la placa ABC123" items={items} />);
    abrir();
    const menu = screen.getByRole("menu");
    expect(menu).toHaveAttribute("data-lado", "arriba");
    expect(parseFloat(menu.style.top)).toBe(rectBoton.top - ALTO_MENU - 4);
    expect(menu.style.maxHeight).toBe("");
    expect(screen.getAllByRole("menuitem")).toHaveLength(2);
  });

  it("A4.3: si no cabe en ningún lado, va al de más espacio con altura máxima y scroll interno", () => {
    altoMenu = 2000;
    rectBoton = { top: 300, bottom: 332, left: 600, right: 700, width: 100, height: 32 };
    render(<MenuAcciones ariaLabel="Acciones de la placa ABC123" items={items} />);
    abrir();
    const menu = screen.getByRole("menu");
    const espacioAbajo = window.innerHeight - rectBoton.bottom - 8;
    expect(menu).toHaveAttribute("data-lado", "abajo");
    expect(parseFloat(menu.style.maxHeight)).toBe(espacioAbajo - 8);
    expect(menu.className).toContain("overflow-y-auto");
  });

  it("el foco inicial sigue yendo al primer ítem habilitado tras medir, y Esc lo devuelve al disparador", () => {
    rectBoton = { top: window.innerHeight - 72, bottom: window.innerHeight - 40, left: 600, right: 700, width: 100, height: 32 };
    render(<MenuAcciones ariaLabel="Acciones de la placa ABC123" items={items} />);
    const disparador = abrir();
    expect(screen.getByRole("menuitem", { name: "Ver dictamen" })).toHaveFocus();
    fireEvent.keyDown(screen.getByRole("menuitem", { name: "Ver dictamen" }), { key: "Escape" });
    expect(disparador).toHaveFocus();
  });

  it("grupo: entre grupos distintos hay un separador; sin `grupo` no hay ninguno (no regresión)", () => {
    const { unmount } = render(
      <MenuAcciones
        ariaLabel="Acciones de la placa ABC123"
        items={[
          { key: "d", label: "Ver dictamen", onSelect: vi.fn(), grupo: "dictamen" },
          { key: "au", label: "Auditoría del dictamen", onSelect: vi.fn(), grupo: "dictamen" },
          { key: "s", label: "Sanear", onSelect: vi.fn(), grupo: "operacion" },
        ]}
      />,
    );
    abrir();
    expect(within(screen.getByRole("menu")).getAllByRole("separator")).toHaveLength(1);
    const [d, au, s] = screen.getAllByRole("menuitem");
    fireEvent.keyDown(d, { key: "ArrowDown" });
    expect(au).toHaveFocus();
    fireEvent.keyDown(au, { key: "ArrowDown" });
    expect(s).toHaveFocus();
    unmount();
    render(<MenuAcciones ariaLabel="Acciones de la placa ABC123" items={items} />);
    abrir();
    expect(within(screen.getByRole("menu")).queryAllByRole("separator")).toHaveLength(0);
  });
});
