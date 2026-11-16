// HU #14583 (spec-ui §9, A9.1–A9.2): `distribucion="equilibrada"` reparte los grupos visibles en orden de catálogo,
// ceil(n/2) a la izquierda del botón de inicio y el resto a la derecha, sin espaciadores (la cápsula mide su contenido).
// Sin el prop (o con "lados"), el dock es el de siempre: cada grupo en su `side` y el lado corto relleno con espaciadores.
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { Box } from "lucide-react";
import { buildDock, type NavCatalog } from "../nav";
import { Dock } from "../dock/Dock";
import { DockMobile } from "../dock/DockMobile";

// Uso de ejemplo: <Dock groups={buildDock(nav, viewer, pathname)} distribucion="equilibrada" homeHref="/" homeLabel="Inicio"
//   homeIconSrc="/favicon.svg" homeActive={false} /> → con 5 grupos: 3 | Inicio | 2.

vi.mock("next/navigation", () => ({ usePathname: () => "/" }));
afterEach(cleanup);

const viewer = { permissions: [], isSuperAdmin: false };
const NOMBRES = ["Diagnósticos", "Reportes", "Backoffice", "Usuarios", "Configuración"];

/** n secciones de una sola entrada; todas declaran `side: "left"` salvo la última, como el catálogo de Diagnóstico. */
function catalogo(n: number): NavCatalog {
  const nombres = NOMBRES.slice(0, n);
  return {
    sections: nombres.map((label, i) => ({ id: `s${i}`, label, icon: Box, side: i === n - 1 && n > 1 ? "right" : "left" })),
    items: nombres.map((label, i) => ({ key: `k${i}`, label, href: `/r${i}`, section: `s${i}`, icon: Box })),
  };
}

function pintar(n: number, distribucion?: "lados" | "equilibrada") {
  const groups = buildDock(catalogo(n), viewer, "/");
  render(<Dock groups={groups} distribucion={distribucion} homeHref="/" homeLabel="Inicio" homeIconSrc="/favicon.svg" homeActive={false} />);
  const nav = screen.getByRole("navigation", { name: "Navegación principal" });
  const hijos = Array.from(nav.children);
  const fab = hijos.findIndex((el) => el.getAttribute("aria-label") === "Inicio");
  const esPildora = (el: Element) => el.tagName === "A" || el.querySelector("button") !== null;
  return {
    izquierda: hijos.slice(0, fab).filter(esPildora).map((el) => el.textContent),
    derecha: hijos.slice(fab + 1).filter(esPildora).map((el) => el.textContent),
    espaciadores: hijos.filter((el) => el.tagName === "SPAN" && el.getAttribute("aria-hidden") === "true").length,
  };
}

describe("Dock distribucion=equilibrada (HU #14583)", () => {
  it("A9.1: 4 grupos → 2 | 2, en orden de catálogo e ignorando `side`, sin espaciadores", () => {
    const d = pintar(4, "equilibrada");
    expect(d.izquierda).toEqual(["Diagnósticos", "Reportes"]);
    expect(d.derecha).toEqual(["Backoffice", "Usuarios"]);
    expect(d.espaciadores).toBe(0);
  });

  it("A9.1: 5 grupos → 3 | 2 (impar: sobra a la izquierda)", () => {
    const d = pintar(5, "equilibrada");
    expect(d.izquierda).toEqual(["Diagnósticos", "Reportes", "Backoffice"]);
    expect(d.derecha).toEqual(["Usuarios", "Configuración"]);
    expect(d.espaciadores).toBe(0);
  });

  it("A9.1: 1 grupo → 1 | 0, sin espaciador al otro lado", () => {
    const d = pintar(1, "equilibrada");
    expect(d.izquierda).toEqual(["Diagnósticos"]);
    expect(d.derecha).toEqual([]);
    expect(d.espaciadores).toBe(0);
  });

  it("el orden de Tab sigue al visual: izquierda → Inicio → derecha", () => {
    const groups = buildDock(catalogo(4), viewer, "/");
    render(<Dock groups={groups} distribucion="equilibrada" homeHref="/" homeLabel="Inicio" homeIconSrc="/favicon.svg" homeActive={false} />);
    const enlaces = screen.getAllByRole("link").map((a) => a.getAttribute("aria-label"));
    expect(enlaces).toEqual(["Diagnósticos", "Reportes", "Inicio", "Backoffice", "Usuarios"]);
  });
});

describe("Dock sin distribucion: comportamiento de siempre (A9.2, otros productos)", () => {
  it("sin el prop, cada grupo va a su `side` y el lado corto se rellena con espaciadores", () => {
    const d = pintar(4);
    expect(d.izquierda).toEqual(["Diagnósticos", "Reportes", "Backoffice"]);
    expect(d.derecha).toEqual(["Usuarios"]);
    expect(d.espaciadores).toBe(2);
  });

  it("con distribucion=lados el DOM es idéntico al de sin prop", () => {
    const groups = buildDock(catalogo(5), viewer, "/");
    const sinProp = render(<Dock groups={groups} homeHref="/" homeLabel="Inicio" homeIconSrc="/favicon.svg" homeActive={false} />).container.innerHTML;
    cleanup();
    const lados = render(<Dock groups={groups} distribucion="lados" homeHref="/" homeLabel="Inicio" homeIconSrc="/favicon.svg" homeActive={false} />).container.innerHTML;
    const sinIds = (html: string) => html.replace(/(id|aria-describedby|aria-controls|aria-labelledby)="[^"]*"/g, '$1="X"');
    expect(sinIds(lados)).toBe(sinIds(sinProp));
  });

  it("DockMobile no se reparte: la hoja lista los mismos grupos en orden de catálogo", () => {
    const groups = buildDock(catalogo(4), viewer, "/");
    expect(() => render(<DockMobile groups={groups} homeIconSrc="/favicon.svg" />)).not.toThrow();
  });
});
