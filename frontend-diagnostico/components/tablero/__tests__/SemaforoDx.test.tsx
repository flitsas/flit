import { describe, expect, it } from "vitest";
import { render, within } from "@testing-library/react";
import { CeldaSemaforo, ChipEstadoCelda } from "@/components/tablero/CeldaSemaforo";
import { celda } from "@/lib/tablero/__tests__/datos";
import type { CeldaGrilla } from "@/lib/tablero/api";

// Uso de ejemplo: <ChipEstadoCelda estado="COMPLETADO" /> pinta la etiqueta del kit «Ejecutado» con IconoDxEjecutado y el tono
// --dx-estado-ejecutado; <CeldaSemaforo solicitada={false} /> pinta «Sin solicitar» como NA con su motivo propio (AC12).

function pintarCelda(props: Partial<Parameters<typeof CeldaSemaforo>[0]> & { celda?: CeldaGrilla }) {
  return render(
    <table>
      <tbody>
        <tr>
          <CeldaSemaforo
            fuente="IMPUESTO_DEPARTAMENTAL"
            nombreFuente="Impuesto departamental"
            identificador="TST001"
            puedeReprocesar={false}
            {...props}
          />
        </tr>
      </tbody>
    </table>,
  );
}

const chip = (raiz: HTMLElement) => raiz.querySelector<HTMLElement>("[data-tono]")!;

describe("Semáforo — HU #14562 AC11: etiqueta del kit con IconoDx y tono --dx-estado-*", () => {
  it.each([
    ["COMPLETADO", undefined, "Ejecutado", "ejecutado"],
    ["COMPLETADO", { tiene_deuda: true }, "Con deuda", "deuda"],
    ["ERROR", { tipo_error: "TIMEOUT" }, "Error", "error"],
    ["EN_CURSO", undefined, "Sin ejecutar (en curso)", "en-curso"],
    ["PENDIENTE", undefined, "Sin ejecutar", "pendiente"],
    ["NA", { motivo_na: "SERVICIO_PUBLICO" }, "Novedad", "novedad"],
  ] as const)("%s %j → «%s» con tono %s, data-estado y data-tono", (estado, cambios, rotulo, tono) => {
    const { container } = pintarCelda({ celda: celda(1, "IMPUESTO_DEPARTAMENTAL", estado, { ...(cambios ?? {}) }) });
    const td = container.querySelector("td")!;
    expect(td).toHaveAttribute("data-estado", estado);
    const etiqueta = chip(td);
    expect(etiqueta).toHaveAttribute("data-tono", tono);
    expect(etiqueta).toHaveTextContent(new RegExp(`^${rotulo.replace(/[()]/g, "\\$&")}$`));
    expect(etiqueta.style.getPropertyValue("--c")).toBe(`var(--dx-estado-${tono}-borde)`);
    expect(etiqueta.style.getPropertyValue("--ct")).toBe(`var(--dx-estado-${tono})`);
    const icono = etiqueta.querySelector("svg")!;
    expect(icono).toHaveAttribute("aria-hidden", "true");
    // Familia IconoDx: glifo propio sobre la rejilla de 1024 de la base (sin glifos de terceros).
    expect(icono).toHaveAttribute("viewBox", "0 0 1024 1024");
  });

  it("AC5: ningún ícono del semáforo gira dentro de la grilla (EN_CURSO tampoco)", () => {
    const { container } = render(<ChipEstadoCelda estado="EN_CURSO" />);
    expect(container.querySelector("svg")!.getAttribute("class") ?? "").not.toMatch(/animate-/);
  });

  it("D4(4): «Pendiente» toma el color del token del kit 2026 (--dx-estado-pendiente)", () => {
    const { container } = render(<ChipEstadoCelda estado="PENDIENTE" />);
    expect(chip(container).style.getPropertyValue("--ct")).toBe("var(--dx-estado-pendiente)");
  });

  it("«Manual» no aparece en la vista de la compañía (sin `marca`)", () => {
    const { container } = pintarCelda({ celda: celda(1, "IMPUESTO_DEPARTAMENTAL", "COMPLETADO") });
    expect(container).not.toHaveTextContent(/manual/i);
  });
});

describe("Semáforo — HU #14562 AC12: «Sin solicitar» es NA con rótulo y motivo propios", () => {
  it("la fuente no pedida usa el estado NA, el rótulo «Sin solicitar» y su motivo, distintos de la Novedad", () => {
    const { container } = pintarCelda({ solicitada: false });
    const td = container.querySelector("td")!;
    expect(td).toHaveAttribute("data-estado", "NA");
    expect(td).toHaveAttribute("data-motivo", "SIN_SOLICITAR");
    expect(chip(td)).toHaveAttribute("data-tono", "sin-solicitar");
    expect(chip(td)).toHaveTextContent(/^Sin solicitar$/);
    expect(td).toHaveTextContent("No se pidió en este lote");
    expect(within(td).queryByRole("link")).toBeNull();

    const novedad = pintarCelda({ celda: celda(2, "IMPUESTO_DEPARTAMENTAL", "NA", { motivo_na: "SERVICIO_PUBLICO" }) }).container.querySelector("td")!;
    expect(chip(novedad)).toHaveAttribute("data-tono", "novedad");
    expect(novedad).not.toHaveTextContent("No se pidió en este lote");
  });
});
