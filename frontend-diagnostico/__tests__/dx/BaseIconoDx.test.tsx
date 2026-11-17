import { readdirSync } from "node:fs";
import { join, resolve } from "node:path";
import type { ComponentType } from "react";
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { BaseIconoDx, type IconoDxProps } from "@/components/dx/iconos/BaseIconoDx";

// Uso de ejemplo: <IconoDxError /> junto al texto «Error» (decorativo) · <IconoDxCampana titulo="Avisos" /> si va solo.

const DIR = resolve(__dirname, "../../components/dx/iconos");
const iconos = readdirSync(DIR).filter((n) => /^IconoDx\w+\.tsx$/.test(n));

async function cargar(archivo: string): Promise<ComponentType<Omit<IconoDxProps, "children">>> {
  const nombre = archivo.replace(/\.tsx$/, "");
  const modulo = (await import(join(DIR, archivo))) as Record<string, ComponentType<Omit<IconoDxProps, "children">>>;
  return modulo[nombre];
}

describe("AC4 · BaseIconoDx accesible", () => {
  it("sin titulo es decorativo: aria-hidden y focusable=false, sin role", () => {
    const { container } = render(
      <BaseIconoDx>
        <path d="M0 0h10" />
      </BaseIconoDx>,
    );
    const svg = container.querySelector("svg") as SVGSVGElement;
    expect(svg.getAttribute("aria-hidden")).toBe("true");
    expect(svg.getAttribute("focusable")).toBe("false");
    expect(svg.getAttribute("role")).toBeNull();
    expect(svg.getAttribute("aria-label")).toBeNull();
  });

  it("con titulo va solo: role=img y aria-label igual al titulo", () => {
    render(
      <BaseIconoDx titulo="Avisos">
        <path d="M0 0h10" />
      </BaseIconoDx>,
    );
    const img = screen.getByRole("img", { name: "Avisos" });
    expect(img.getAttribute("aria-hidden")).toBeNull();
    expect(img.getAttribute("focusable")).toBe("false");
  });

  it("toma el tamaño 16, 20 o 24 y por defecto 20; el trazo hereda currentColor", () => {
    const { container, rerender } = render(
      <BaseIconoDx>
        <path d="M0 0h10" />
      </BaseIconoDx>,
    );
    const svg = () => container.querySelector("svg") as SVGSVGElement;
    expect(svg().getAttribute("width")).toBe("20");
    rerender(
      <BaseIconoDx tamano={16}>
        <path d="M0 0h10" />
      </BaseIconoDx>,
    );
    expect(svg().getAttribute("width")).toBe("16");
    expect(svg().getAttribute("height")).toBe("16");
    expect(svg().getAttribute("stroke")).toBe("currentColor");
  });

  it("pasa la className al svg", () => {
    const { container } = render(
      <BaseIconoDx className="text-dx-estado-error">
        <path d="M0 0h10" />
      </BaseIconoDx>,
    );
    expect(container.querySelector("svg")?.getAttribute("class")).toContain("text-dx-estado-error");
  });
});

describe("AC4 · cada IconoDx hereda la accesibilidad de la base", () => {
  it("todos son decorativos sin titulo y role=img con titulo", async () => {
    expect(iconos.length).toBeGreaterThan(0);
    for (const archivo of iconos) {
      const Icono = await cargar(archivo);
      const { container, unmount } = render(<Icono />);
      const svg = container.querySelector("svg") as SVGSVGElement;
      expect(svg, archivo).not.toBeNull();
      expect(svg.getAttribute("aria-hidden"), archivo).toBe("true");
      expect(svg.getAttribute("focusable"), archivo).toBe("false");
      expect(svg.querySelectorAll("path,circle,rect,polyline,line").length, archivo).toBeGreaterThan(0);
      unmount();

      const conTitulo = render(<Icono titulo={`Ícono ${archivo}`} />);
      expect(screen.getByRole("img", { name: `Ícono ${archivo}` }), archivo).toBeInTheDocument();
      conTitulo.unmount();
    }
  });
});
