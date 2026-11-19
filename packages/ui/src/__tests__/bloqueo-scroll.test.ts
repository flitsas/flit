import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { adquirirBloqueoScroll } from "../useBloqueoScroll";

// Documento de juguete: el kit corre en Node sin DOM; basta con los estilos que toca el bloqueo.
function montarDom(scrollY = 420, conContenedor = true) {
  // Como un CSSStyleDeclaration real: las propiedades sin valor leen "".
  const vacio = { position: "", top: "", left: "", right: "", width: "", overflow: "", paddingRight: "" };
  const body = { style: { ...vacio } as Record<string, string> };
  const contenedor = { style: { overflow: "auto" } as Record<string, string> };
  const scrollTo = vi.fn();
  vi.stubGlobal("window", { scrollY, innerWidth: 1280, scrollTo });
  vi.stubGlobal("document", {
    body,
    documentElement: { clientWidth: 1265 },
    querySelector: () => (conContenedor ? contenedor : null),
  });
  return { body, contenedor, scrollTo };
}

describe("adquirirBloqueoScroll (iOS: body fijo en vez de overflow hidden)", () => {
  beforeEach(() => vi.unstubAllGlobals());
  afterEach(() => vi.unstubAllGlobals());

  it("fija body en la posición actual, compensa la barra y bloquea el contenedor del shell", () => {
    const { body, contenedor } = montarDom();
    const soltar = adquirirBloqueoScroll();
    expect(body.style.position).toBe("fixed");
    expect(body.style.top).toBe("-420px");
    expect(body.style.paddingRight).toBe("15px");
    expect(contenedor.style.overflow).toBe("hidden");
    soltar();
  });

  it("al soltar restaura los estilos previos y vuelve al mismo scroll", () => {
    const { body, contenedor, scrollTo } = montarDom();
    const soltar = adquirirBloqueoScroll();
    soltar();
    expect(body.style.position).toBe("");
    expect(body.style.top).toBe("");
    expect(contenedor.style.overflow).toBe("auto");
    expect(scrollTo).toHaveBeenCalledWith(0, 420);
  });

  it("con capas anidadas solo libera al cerrar la última, y soltar dos veces no descuadra el conteo", () => {
    const { body, scrollTo } = montarDom();
    const panel = adquirirBloqueoScroll();
    const modal = adquirirBloqueoScroll();
    modal();
    modal();
    expect(body.style.position).toBe("fixed");
    expect(scrollTo).not.toHaveBeenCalled();
    panel();
    expect(body.style.position).toBe("");
    expect(scrollTo).toHaveBeenCalledTimes(1);
  });

  it("sin contenedor del shell (persona natural) solo fija body", () => {
    const { body } = montarDom(0, false);
    const soltar = adquirirBloqueoScroll();
    expect(body.style.position).toBe("fixed");
    soltar();
  });
});
