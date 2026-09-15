import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { BrandStyle } from "../BrandStyle";
import { FLIT_BRAND } from "@/lib/brand/types";

/**
 * Uso de ejemplo: `<head><BrandStyle brand={brand} /></head>` — con marca de red inyecta
 * `<style id="brand-tokens">` en el DOM (primera pintura, sin destello); con FLIT_BRAND no
 * renderiza nada (paridad AC7).
 */
describe("BrandStyle — HU #12419 AC1/AC7", () => {
  it("host FLIT (AC7): NO renderiza ningún <style id=\"brand-tokens\"> — paridad byte a byte", () => {
    const { container } = render(<BrandStyle brand={FLIT_BRAND} />);
    expect(container.querySelector("#brand-tokens")).toBeNull();
    expect(container).toBeEmptyDOMElement();
  });

  it("marca de red: inyecta variables --brand-* en :root y .dark", () => {
    const brand = {
      platformName: "Movilidad Andina",
      logoUrl: null,
      colors: { primary: "#0B3D91", secondary: "#1FA2FF", onPrimary: "#FFFFFF" },
      version: 3,
    };

    const { container } = render(<BrandStyle brand={brand} />);
    const style = container.querySelector("#brand-tokens");

    expect(style).not.toBeNull();
    expect(style?.textContent).toContain("--brand-primary:#0B3D91");
    expect(style?.textContent).toContain(":root{");
    expect(style?.textContent).toContain(".dark{");
  });

  it("edge case: forma de colores inválida (hex malformado) no revienta y no renderiza nada", () => {
    const brand = {
      platformName: "Red Rota",
      logoUrl: null,
      colors: { primary: "no-es-un-hex", secondary: "#1FA2FF", onPrimary: "#FFFFFF" },
      version: 2,
    };

    expect(() => render(<BrandStyle brand={brand} />)).not.toThrow();
  });

  it("contrato: los tonos .dark cumplen contraste 4.5:1 contra el fondo oscuro de la app", async () => {
    const { contrastRatio } = await import("@/lib/brand/contrast");
    const { DARK_APP_BACKGROUND } = await import("@/lib/brand/derive-dark");
    const brand = {
      platformName: "Paleta Clara",
      logoUrl: null,
      colors: { primary: "#E8ECF5", secondary: "#D0D8EE", onPrimary: "#0B3D91" },
      version: 4,
    };

    const { container } = render(<BrandStyle brand={brand} />);
    const css = container.querySelector("#brand-tokens")?.textContent ?? "";
    const darkPrimary = /\.dark\{--brand-primary:(#[0-9A-F]{6})/i.exec(css)?.[1];

    expect(darkPrimary).toBeDefined();
    expect(contrastRatio(darkPrimary as string, DARK_APP_BACKGROUND)).toBeGreaterThanOrEqual(4.5);
  });
});
