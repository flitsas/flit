// Botón de Inicio del dock: en FLIT el isotipo de siempre; en una Marca Blanca su logo, con la
// forma ajustada a la proporción de la imagen (símbolo → círculo, logo con texto → pastilla).
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, render, screen } from "@testing-library/react";
import { BrandProvider } from "@flit/brand/BrandProvider";
import type { Brand } from "@flit/brand/types";
import { useDockHomeMark } from "../dock/DockHomeMark";

afterEach(cleanup);

const COLORS = { primary: "#064E3B", secondary: "#047857", onPrimary: "#FFFFFF" };

function Probe() {
  const mark = useDockHomeMark("/assets/favicon.svg");
  return (
    <div data-testid="home" data-shape={mark.shape}>
      {mark.content}
    </div>
  );
}

// jsdom no carga imágenes: se simula el tamaño natural del logo que «descarga» el hook.
let natural = { w: 0, h: 0 };
class FakeImage {
  onload: (() => void) | null = null;
  naturalWidth = 0;
  naturalHeight = 0;
  set src(_url: string) {
    this.naturalWidth = natural.w;
    this.naturalHeight = natural.h;
    queueMicrotask(() => this.onload?.());
  }
}

beforeEach(() => {
  vi.stubGlobal("Image", FakeImage);
});
afterEach(() => vi.unstubAllGlobals());

async function renderWith(brand: Brand) {
  render(
    <BrandProvider brand={brand}>
      <Probe />
    </BrandProvider>,
  );
  await act(async () => {
    await Promise.resolve();
  });
  return screen.getByTestId("home");
}

const red = (logoUrl: string | null): Brand => ({ platformName: "Movilidad Andina", logoUrl, colors: COLORS, version: 2 });

describe("useDockHomeMark", () => {
  it("FLIT: el isotipo de siempre", async () => {
    const home = await renderWith({ platformName: "FLIT 2.0", logoUrl: null, colors: COLORS, version: 0 });
    expect(home.dataset.shape).toBe("flit");
    expect(home.querySelector("img")?.getAttribute("src")).toBe("/assets/favicon.svg");
  });

  it("logo solo (casi cuadrado): círculo con el logo de la marca", async () => {
    natural = { w: 512, h: 512 };
    const home = await renderWith(red("/api/v1/public/branding/logos/abc"));
    expect(home.dataset.shape).toBe("circle");
    expect(home.querySelector("img")?.getAttribute("src")).toBe("/api/v1/public/branding/logos/abc");
  });

  it("logo con texto (horizontal): pastilla con el logo completo", async () => {
    natural = { w: 1200, h: 320 };
    const home = await renderWith(red("/api/v1/public/branding/logos/wide"));
    expect(home.dataset.shape).toBe("pill");
    expect(home.querySelector("img")?.className).toContain("object-contain");
  });

  it("sin logo publicado: la inicial de la plataforma", async () => {
    const home = await renderWith(red(null));
    expect(home.dataset.shape).toBe("initial");
    expect(home).toHaveTextContent("M");
  });
});
