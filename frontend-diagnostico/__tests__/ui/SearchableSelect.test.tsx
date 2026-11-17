import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { SearchableSelect } from "@flit/ui/SearchableSelect";

// Uso de ejemplo: <SearchableSelect label="Empresa" options={[{ value: "a", label: "Transportes A SAS" }]} value=""
//   defaultLabel="Todas las compañías" onChange={setEmpresa} />
// H-5 (E2E saneamiento): con el teclado, el combobox mostraba el foco solo con el borde de 1 px. Lleva el contorno de
// foco del kit, sin un `outline-none` base que en Tailwind 4 lo anularía.

const clases = (cls: string) => cls.split(/\s+/).filter(Boolean);

describe("SearchableSelect — foco visible (H-5)", () => {
  it("el combobox lleva el contorno de foco del kit y no lo anula con outline-none", () => {
    render(<SearchableSelect label="Empresa" options={[{ value: "a", label: "Transportes A SAS" }]} value="" defaultLabel="Todas" onChange={vi.fn()} />);
    const campo = screen.getByRole("combobox", { name: "Empresa" });
    expect(clases(campo.className)).not.toContain("outline-none");
    expect(clases(campo.className)).toContain("focus-visible:outline-2");
    expect(clases(campo.className)).toContain("focus-visible:outline-[var(--di-action)]");
  });
});
