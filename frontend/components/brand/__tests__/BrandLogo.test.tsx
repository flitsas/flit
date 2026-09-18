import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { BrandLogo } from "../BrandLogo";
import { BrandProvider } from "../BrandProvider";
import { FLIT_BRAND } from "@/lib/brand/types";

/**
 * Uso de ejemplo:
 *   <BrandLogo variant="white" className="h-10 w-auto" />               // vía contexto
 *   <BrandLogo brand={brand} variant="dark" alt="Movilidad Andina" />    // override explícito
 */
describe("BrandLogo", () => {
  it("en host FLIT usa el asset actual (blanco) — paridad AC7", () => {
    render(
      <BrandProvider brand={FLIT_BRAND}>
        <BrandLogo variant="white" className="h-10 w-auto" />
      </BrandProvider>,
    );

    const img = screen.getByRole("img", { name: "FLIT 2.0" });
    expect(img).toHaveAttribute("src", "/assets/logo-flit-white.svg");
    expect(img).toHaveClass("h-10", "w-auto");
  });

  it("en host FLIT variante 'dark' usa el asset oscuro actual", () => {
    render(
      <BrandProvider brand={FLIT_BRAND}>
        <BrandLogo variant="dark" />
      </BrandProvider>,
    );

    expect(screen.getByRole("img")).toHaveAttribute("src", "/assets/logo-flit-dark.svg");
  });

  it("con marca de red, usa brand.logoUrl para AMBAS variantes (un solo logo)", () => {
    const brand = {
      platformName: "Movilidad Andina",
      logoUrl: "/api/v1/public/branding/logos/abc",
      colors: { primary: "#0B3D91", secondary: "#1FA2FF", onPrimary: "#FFFFFF" },
      version: 3,
    };

    render(
      <BrandProvider brand={brand}>
        <BrandLogo variant="white" />
      </BrandProvider>,
    );

    const img = screen.getByRole("img", { name: "Movilidad Andina" });
    expect(img).toHaveAttribute("src", "/api/v1/public/branding/logos/abc");
  });

  it("respeta un override explícito de `brand` aunque el contexto sea distinto (Server Component)", () => {
    const overrideBrand = {
      platformName: "Otra Red",
      logoUrl: "/api/v1/public/branding/logos/xyz",
      colors: FLIT_BRAND.colors,
      version: 5,
    };

    render(
      <BrandProvider brand={FLIT_BRAND}>
        <BrandLogo brand={overrideBrand} variant="white" />
      </BrandProvider>,
    );

    expect(screen.getByRole("img", { name: "Otra Red" })).toHaveAttribute(
      "src",
      "/api/v1/public/branding/logos/xyz",
    );
  });

  it("edge case: alt explícito gana sobre platformName", () => {
    render(
      <BrandProvider brand={FLIT_BRAND}>
        <BrandLogo variant="white" alt="FLIT" />
      </BrandProvider>,
    );

    expect(screen.getByRole("img", { name: "FLIT" })).toBeInTheDocument();
  });

  it("no lanza sin BrandProvider ancestro (respaldo FLIT del contexto)", () => {
    expect(() => render(<BrandLogo variant="white" />)).not.toThrow();
    expect(screen.getByRole("img")).toHaveAttribute("src", "/assets/logo-flit-white.svg");
  });
});
