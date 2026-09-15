import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { BrandProvider, useBrand } from "../BrandProvider";
import { FLIT_BRAND } from "@/lib/brand/types";

/**
 * Uso de ejemplo:
 *   <BrandProvider brand={brand}><Consumer/></BrandProvider>
 *   const { platformName, isFlit } = useBrand(); // dentro de Consumer
 */
function Probe() {
  const brand = useBrand();
  return (
    <div>
      <span data-testid="name">{brand.platformName}</span>
      <span data-testid="is-flit">{String(brand.isFlit)}</span>
      <span data-testid="logo">{brand.logoUrl ?? "null"}</span>
    </div>
  );
}

describe("BrandProvider / useBrand", () => {
  it("renderiza sin errores y expone la marca de red inyectada", () => {
    const networkBrand = {
      platformName: "Movilidad Andina",
      logoUrl: "/api/v1/public/branding/logos/abc",
      colors: { primary: "#0B3D91", secondary: "#1FA2FF", onPrimary: "#FFFFFF" },
      version: 3,
    };

    render(
      <BrandProvider brand={networkBrand}>
        <Probe />
      </BrandProvider>,
    );

    expect(screen.getByTestId("name")).toHaveTextContent("Movilidad Andina");
    expect(screen.getByTestId("is-flit")).toHaveTextContent("false");
    expect(screen.getByTestId("logo")).toHaveTextContent("/api/v1/public/branding/logos/abc");
  });

  it("con FLIT_BRAND, isFlit es true (edge case: identidad FLIT explícita)", () => {
    render(
      <BrandProvider brand={FLIT_BRAND}>
        <Probe />
      </BrandProvider>,
    );

    expect(screen.getByTestId("is-flit")).toHaveTextContent("true");
    expect(screen.getByTestId("name")).toHaveTextContent("FLIT 2.0");
  });

  it("fuera de un BrandProvider, useBrand() devuelve FLIT_BRAND (valor por defecto del contexto, sin lanzar)", () => {
    expect(() => render(<Probe />)).not.toThrow();
    expect(screen.getByTestId("name")).toHaveTextContent("FLIT 2.0");
    expect(screen.getByTestId("is-flit")).toHaveTextContent("true");
  });

  it("contrato: isFlit se determina por version===0, no por nombre (una red no se confunde con FLIT)", () => {
    const namedLikeFlit = { platformName: "FLIT 2.0", logoUrl: null, colors: FLIT_BRAND.colors, version: 1 };
    render(
      <BrandProvider brand={namedLikeFlit}>
        <Probe />
      </BrandProvider>,
    );
    expect(screen.getByTestId("is-flit")).toHaveTextContent("false");
  });
});
