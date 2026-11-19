import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { CHIP_EMBUDO_CLS, CHIP_FILTRO_CLS, ChipFiltro } from "@flit/ui/ChipsFiltro";

// Uso de ejemplo: <ChipFiltro etiqueta="Con saneamiento" activo={filtro === "con"} onClick={() => setFiltro("con")} />
//   y, en el embudo, <ChipFiltro cifra={12} etiqueta="Solicitado" activo={false} onClick={…} />.
// H-2 (E2E saneamiento, WCAG 2.4.7): en Tailwind 4 `focus:outline-none` deja `--tw-outline-style: none` y el
// `focus-visible:outline-2` hereda ese estilo, así que el contorno de foco nunca se pinta. El chip no lo lleva.

const clases = (cls: string) => cls.split(/\s+/).filter(Boolean);

describe("ChipFiltro — foco visible (H-2)", () => {
  it.each([
    ["sin cifra", CHIP_FILTRO_CLS],
    ["con cifra (embudo)", CHIP_EMBUDO_CLS],
  ])("el chip %s pinta el contorno con focus-visible y no lo anula con outline-none", (_caso, cls) => {
    expect(clases(cls)).not.toContain("focus:outline-none");
    expect(clases(cls)).not.toContain("outline-none");
    expect(clases(cls)).toContain("focus-visible:outline-2");
    expect(clases(cls)).toContain("focus-visible:outline-[var(--di-action)]");
  });

  it("el botón renderizado lleva el contorno de foco del kit", () => {
    render(<ChipFiltro etiqueta="Con saneamiento" activo onClick={vi.fn()} />);
    const chip = screen.getByRole("button", { name: "Con saneamiento" });
    expect(chip).toHaveAttribute("aria-pressed", "true");
    expect(chip.className).toContain("focus-visible:outline-2");
    expect(clases(chip.className)).not.toContain("focus:outline-none");
  });
});
