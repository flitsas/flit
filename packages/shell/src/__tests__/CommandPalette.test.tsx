import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { Box } from "lucide-react";
import type { AnchorHTMLAttributes } from "react";
import { CommandPalette } from "../CommandPalette";
import { buildDock, type NavCatalog } from "../nav";

// next/link fuera de una app real: un <a> que no navega, para ver a dónde iría cada opción.
const navegado = vi.fn();
vi.mock("next/link", () => ({
  default: ({ href, onClick, ...rest }: AnchorHTMLAttributes<HTMLAnchorElement> & { href: string }) => (
    <a
      href={href}
      {...rest}
      onClick={(e) => {
        e.preventDefault();
        navegado(href);
        onClick?.(e);
      }}
    />
  ),
}));

beforeEach(() => {
  navegado.mockReset();
  vi.spyOn(window, "requestAnimationFrame").mockImplementation((cb) => {
    cb(0);
    return 0;
  });
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

const catalog: NavCatalog = {
  sections: [{ id: "operacion", label: "Operación", icon: Box, side: "left" }],
  items: [
    { key: "tramites", label: "Trámites", href: "/tramites", section: "operacion", icon: Box },
    { key: "reportes", label: "Reportes", href: "/?m=reportes", section: "operacion", icon: Box },
    { key: "usuarios", label: "Usuarios", href: "/?m=usuarios", section: "operacion", icon: Box, permission: "usuarios.ver" },
  ],
};
const groups = buildDock(catalog, { permissions: [], isSuperAdmin: false }, "/");

function renderPalette() {
  return render(
    <>
      <button type="button">Origen</button>
      <CommandPalette
        groups={groups}
        home={{ href: "/", label: "Inicio Trámites" }}
        actions={[{ key: "sin_firmas", label: "Trámites sin firmas", href: "/tramites?atajo=sin_firmas", requires: "tramites" }]}
        plate={{ href: (p) => `/tramites?placa=${p}`, hint: "Buscar en Trámites", requires: "tramites" }}
      />
    </>,
  );
}

const combo = () => screen.getByRole("combobox", { name: "Buscar una sección, una acción o una placa" });
const activeOption = () => document.getElementById(combo().getAttribute("aria-activedescendant") ?? "");

describe("CommandPalette (HU #14480)", () => {
  it("Ctrl K y ⌘K lo abren con el foco en el campo; Escape lo cierra y devuelve el foco (AC3)", () => {
    renderPalette();
    const origen = screen.getByRole("button", { name: "Origen" });
    origen.focus();

    fireEvent.keyDown(document, { key: "k", ctrlKey: true });
    expect(screen.getByRole("dialog", { name: "Buscador" })).toBeInTheDocument();
    expect(combo()).toHaveFocus();

    fireEvent.keyDown(combo(), { key: "Escape" });
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(origen).toHaveFocus();

    fireEvent.keyDown(document, { key: "K", metaKey: true });
    expect(screen.getByRole("dialog", { name: "Buscador" })).toBeInTheDocument();
  });

  it("el botón visible de la barra también lo abre, y el foco vuelve a él al cerrar", () => {
    renderPalette();
    const boton = screen.getByRole("button", { name: "Buscar (Ctrl K)" });
    expect(boton).toHaveAttribute("aria-keyshortcuts", "Control+K Meta+K");
    boton.focus();
    fireEvent.click(boton);
    expect(combo()).toHaveFocus();
    fireEvent.keyDown(combo(), { key: "Escape" });
    expect(boton).toHaveFocus();
  });

  it("es un combobox con grupos con nombre y solo lo que el rol ve (AC1)", () => {
    renderPalette();
    fireEvent.keyDown(document, { key: "k", ctrlKey: true });
    const lista = screen.getByRole("listbox", { name: "Resultados" });
    expect(combo()).toHaveAttribute("aria-controls", lista.id);

    const secciones = within(lista).getByRole("group", { name: "Secciones" });
    const opciones = within(secciones).getAllByRole("option");
    expect(opciones.map((o) => o.textContent)).toEqual(["InicioInicio Trámites", "TrámitesOperación", "ReportesOperación"]);
    expect(within(secciones).getByRole("option", { name: "Trámites" })).toHaveAccessibleDescription("Operación");
    expect(within(lista).getByRole("group", { name: "Acciones" })).toBeInTheDocument();
    expect(within(lista).queryByText("Usuarios")).toBeNull();

    fireEvent.change(combo(), { target: { value: "usuarios" } });
    expect(within(lista).queryAllByRole("option")).toHaveLength(0);
    expect(screen.getByText(/No encontramos nada con eso/)).toBeInTheDocument();
  });

  it("las flechas recorren (y dan la vuelta) y Enter abre la opción activa", () => {
    renderPalette();
    fireEvent.keyDown(document, { key: "k", ctrlKey: true });
    expect(activeOption()).toHaveTextContent("Inicio");
    expect(activeOption()).toHaveAttribute("aria-selected", "true");

    fireEvent.keyDown(combo(), { key: "ArrowDown" });
    expect(activeOption()).toHaveTextContent("Trámites");
    fireEvent.keyDown(combo(), { key: "ArrowUp" });
    fireEvent.keyDown(combo(), { key: "ArrowUp" });
    expect(activeOption()).toHaveTextContent("Trámites sin firmas");

    fireEvent.keyDown(combo(), { key: "Enter" });
    expect(navegado).toHaveBeenCalledWith("/tramites?atajo=sin_firmas");
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("una placa escrita es el primer resultado y lleva al listado con ?placa= (AC2)", () => {
    renderPalette();
    fireEvent.keyDown(document, { key: "k", ctrlKey: true });
    fireEvent.change(combo(), { target: { value: "abc 12d" } });
    const placas = screen.getByRole("group", { name: "Placas" });
    expect(within(placas).getByRole("option")).toHaveAttribute("href", "/tramites?placa=ABC12D");
    expect(activeOption()).toHaveTextContent("ABC12D");
    fireEvent.keyDown(combo(), { key: "Enter" });
    expect(navegado).toHaveBeenCalledWith("/tramites?placa=ABC12D");
  });

  it("con el ratón: pulsar una opción navega y pulsar fuera cierra", () => {
    renderPalette();
    fireEvent.keyDown(document, { key: "k", ctrlKey: true });
    fireEvent.click(screen.getByRole("option", { name: "Reportes" }));
    expect(navegado).toHaveBeenCalledWith("/?m=reportes");
    expect(screen.queryByRole("dialog")).toBeNull();

    fireEvent.keyDown(document, { key: "k", ctrlKey: true });
    const velo = screen.getByRole("dialog").parentElement!;
    act(() => {
      fireEvent.mouseDown(velo);
    });
    expect(screen.queryByRole("dialog")).toBeNull();
  });
});
