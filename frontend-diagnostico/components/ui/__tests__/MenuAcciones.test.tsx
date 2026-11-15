import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
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
