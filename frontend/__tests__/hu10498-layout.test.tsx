import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { Shell } from "@/components/atom/Shell";

/**
 * HU #10498 — Layout: scroll encuadrado en el área de contenido sin quedar oculto
 * tras el dock (AC1) y wizard nivelado con scroll interno en el stepper (AC2).
 */

vi.mock("next/navigation", () => ({ usePathname: () => "/" }));

const FE_ROOT = path.resolve(__dirname, "..");
const read = (rel: string) => readFileSync(path.join(FE_ROOT, rel), "utf8");

describe("HU #10498 — AC1: scroll encuadrado en el Shell", () => {
  it("el contenido se renderiza dentro de un contenedor scrollable con separación del dock", () => {
    render(
      <Shell active="dashboard" onNav={vi.fn()}>
        <div>contenido largo</div>
      </Shell>,
    );
    const wrapper = screen.getByText("contenido largo").parentElement!;
    expect(wrapper.className).toContain("overflow-y-auto");
    // El padding inferior libera el dock flotante (nada oculto tras él).
    expect(wrapper.className).toMatch(/pb-\d/);
    // El dock sigue presente y flotante sobre el contenido.
    expect(screen.getByRole("button", { name: "Inicio FLIT" })).toBeInTheDocument();
  });

  it("el área de contenido ya no clipa el scroll (no overflow-hidden en el wrapper de children)", () => {
    const src = read("components/atom/Shell.tsx");
    expect(src).not.toMatch(/absolute inset-0 overflow-hidden">\{children\}/);
    expect(src).toMatch(/absolute inset-0 overflow-y-auto pb-\d+">\{children\}/);
  });
});

describe("HU #10498 — AC2: wizard nivelado con scroll interno en el stepper", () => {
  it("StepperForm: columnas niveladas (items-stretch), sin self-start/sticky y lista con scroll propio", () => {
    const src = read("components/atom/StepperForm.tsx");
    expect(src).toMatch(/lg:items-stretch/);
    expect(src).not.toMatch(/lg:self-start/);
    expect(src).not.toMatch(/lg:sticky/);
    // La lista de pasos (ol) es la única con overflow-y-auto.
    expect(src).toMatch(/<ol className="[^"]*overflow-y-auto[^"]*">/);
  });

  it("TramiteWizard (wizard real): stepper nivelado con la misma técnica", () => {
    const src = read("components/operacion/TramiteWizard.tsx");
    expect(src).toMatch(/md:items-stretch/);
    expect(src).not.toMatch(/md:self-start/);
    expect(src).not.toMatch(/md:sticky/);
    expect(src).toMatch(/<ol className="[^"]*overflow-y-auto[^"]*">/);
  });
});
