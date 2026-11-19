import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { TablaEditable, type ColumnaEditable } from "@flit/ui/TablaEditable";
import { TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { PageNav } from "@flit/ui/PageNav";
import { PAGINACION_SELECT_CLS } from "@flit/ui/Pagination";

// Uso de ejemplo: <TarjetaMetrica etiqueta="Con error" cifra={3} onAlternar={…} /> y
//   <TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={…} />.
// H-6 (E2E saneamiento, WCAG 2.4.7): en Tailwind 4 `focus:outline-none` deja `--tw-outline-style: none` y el
// `focus-visible:outline-*` hereda ese estilo, así que el contorno nunca se pinta. Donde el indicador es un
// contorno, no se acompaña de `outline-none`; donde es un anillo (box-shadow), `outline-none` no lo anula.

const clases = (cls: string) => cls.split(/\s+/).filter(Boolean);

interface Fila { id: string; nombre: string }
const filas: Fila[] = [{ id: "A", nombre: "Alfa" }];
const columnas: ColumnaEditable<Fila>[] = [
  { campo: "id", titulo: "Código" },
  { campo: "nombre", titulo: "Nombre", editable: true },
];

describe("Foco visible en el kit (H-6)", () => {
  it("TablaEditable: la celda de estado pinta el contorno con focus-visible y no lo anula", () => {
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} />);
    const estado = within(screen.getByRole("row", { name: "A" })).getByText("Sin cambios");
    const cls = clases(estado.className);
    expect(cls).not.toContain("focus:outline-none");
    expect(cls).toContain("focus-visible:outline-2");
    expect(cls).toContain("focus-visible:outline-[var(--di-action)]");
  });

  it.each([
    ["con onAlternar (filtro)", { onAlternar: vi.fn() }],
    ["con onClick (accionable)", { onClick: vi.fn() }],
  ])("TarjetaMetrica %s pinta el contorno con focus-visible y no lo anula", (_caso, props) => {
    render(<TarjetaMetrica etiqueta="Con error" cifra={3} {...props} />);
    const cls = clases(screen.getByRole("button").className);
    expect(cls).not.toContain("focus:outline-none");
    expect(cls).toContain("focus-visible:outline-2");
    expect(cls).toContain("focus-visible:outline-offset-2");
    expect(cls).toContain("focus-visible:outline-[var(--kit-azul-texto)]");
  });

  it("PageNav: flechas y números marcan el foco con anillo (box-shadow), que outline-none no anula", () => {
    render(<PageNav page={2} totalPages={3} onPageChange={vi.fn()} resumen="Mostrando 26–50 de 60" ariaLabel="Paginación de prueba" />);
    const botones = screen.getAllByRole("button");
    expect(botones.length).toBeGreaterThan(0);
    for (const boton of botones) {
      const cls = clases(boton.className);
      expect(cls).toContain("focus-visible:ring-2");
      expect(cls.some((c) => c.startsWith("focus-visible:outline-"))).toBe(false);
    }
  });

  it("Pagination: el selector de filas marca el foco con borde, sombra del kit y anillo", () => {
    const cls = clases(PAGINACION_SELECT_CLS);
    expect(cls).toContain("focus:border-[color:var(--di-action)]");
    expect(cls).toContain("focus:shadow-[var(--kit-sombra)]");
    expect(cls).toContain("focus-visible:ring-2");
    expect(cls.some((c) => c.startsWith("focus-visible:outline-"))).toBe(false);
  });
});
