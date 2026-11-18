import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { IconoDxBandera } from "@/components/dx/iconos/IconoDxBandera";

// Uso de ejemplo: <IconoDxBandera tamano={16} encendida={estado !== null} /> antes del texto «Sanear» / «Solicitado»
// (saneamiento, spec-ui §1.2). Hueca = sin solicitar; rellena = con saneamiento: el estado se ve por la forma, no solo por color.

const pano = (svg: Element) => svg.querySelectorAll("path")[1];

describe("IconoDxBandera (saneamiento, spec-ui §1.2)", () => {
  it("apagada por defecto: mástil y paño hueco (fill none)", () => {
    const { container } = render(<IconoDxBandera />);
    const svg = container.querySelector("svg")!;
    expect(svg.querySelectorAll("path")).toHaveLength(2);
    expect(pano(svg).getAttribute("fill")).toBe("none");
  });

  it("encendida: el paño se rellena con currentColor", () => {
    const { container } = render(<IconoDxBandera encendida />);
    expect(pano(container.querySelector("svg")!).getAttribute("fill")).toBe("currentColor");
  });

  it("es de la familia: rejilla 1024, trazo en currentColor, decorativa sin titulo y con nombre si lo trae", () => {
    const { container, unmount } = render(<IconoDxBandera tamano={16} />);
    const svg = container.querySelector("svg")!;
    expect(svg.getAttribute("viewBox")).toBe("0 0 1024 1024");
    expect(svg.getAttribute("stroke")).toBe("currentColor");
    expect(svg.getAttribute("width")).toBe("16");
    expect(svg.getAttribute("aria-hidden")).toBe("true");
    unmount();
    render(<IconoDxBandera titulo="Saneamiento" />);
    expect(screen.getByRole("img", { name: "Saneamiento" })).toBeInTheDocument();
  });
});
