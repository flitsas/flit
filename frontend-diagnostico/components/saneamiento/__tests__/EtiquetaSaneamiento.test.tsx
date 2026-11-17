import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { EtiquetaSaneamiento } from "@/components/saneamiento/EtiquetaSaneamiento";

// Uso de ejemplo (HU #14577, spec-ui §1.1): <EtiquetaSaneamiento estado={v.saneamiento?.estado ?? null} /> bajo el veredicto
// de la fila. Punto + texto completo con el tono de lib/saneamiento/estados.ts; sin role="status" (en una tabla de 100 filas
// serían 100 regiones vivas). Sin saneamiento, o ANULADO fuera del backoffice, no pinta nada.

describe("EtiquetaSaneamiento (spec-ui §1.1)", () => {
  it.each([
    ["SOLICITADO", "Saneamiento solicitado", "info"],
    ["EN_GESTION", "En saneamiento", "warning"],
    ["SANEADO", "Saneado", "success"],
    ["NO_PROCEDE", "Saneamiento no procede", "neutral"],
  ] as const)("%s: «%s» con punto y los tokens del tono %s", (estado, texto, tono) => {
    const { container } = render(<EtiquetaSaneamiento estado={estado} />);
    const etiqueta = screen.getByText(texto);
    expect(etiqueta).toHaveClass("kit-etiqueta");
    expect(etiqueta.style.getPropertyValue("--c")).toBe(`var(--badge-${tono}-border)`);
    expect(etiqueta.style.getPropertyValue("--ct")).toBe(`var(--badge-${tono}-fg)`);
    const punto = etiqueta.querySelector(".kit-etiqueta__punto");
    expect(punto).not.toBeNull();
    expect(punto).toHaveAttribute("aria-hidden", "true");
    // Contenido estático: nada de regiones vivas dentro de la tabla.
    expect(container.querySelector("[role='status'], [aria-live]")).toBeNull();
  });

  it("sin saneamiento (null o undefined) no pinta nada", () => {
    expect(render(<EtiquetaSaneamiento estado={null} />).container.innerHTML).toBe("");
    expect(render(<EtiquetaSaneamiento estado={undefined} />).container.innerHTML).toBe("");
  });

  it("ANULADO: nada para la compañía; «Anulado» neutral solo en el backoffice (D-06)", () => {
    expect(render(<EtiquetaSaneamiento estado="ANULADO" />).container.innerHTML).toBe("");
    render(<EtiquetaSaneamiento estado="ANULADO" backoffice />);
    expect(screen.getByText("Anulado").style.getPropertyValue("--c")).toBe("var(--badge-neutral-border)");
  });

  it("un estado desconocido no se pinta: nunca se muestra el código", () => {
    const { container } = render(<EtiquetaSaneamiento estado={"OTRO_ESTADO" as never} />);
    expect(container.innerHTML).toBe("");
  });

  it("el archivo no tiene colores sueltos (solo tokens)", () => {
    const fuente = readFileSync(join(__dirname, "..", "EtiquetaSaneamiento.tsx"), "utf8");
    expect(fuente).not.toMatch(/#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\b/);
    expect(fuente).not.toMatch(/rgba?\(/);
  });
});
