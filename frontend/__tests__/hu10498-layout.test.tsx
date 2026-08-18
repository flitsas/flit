import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { Shell } from "@/components/atom/Shell";

/**
 * HU #10498 — Layout: scroll encuadrado en el área de contenido sin quedar oculto
 * tras el dock (AC1) y wizard con scroll en main + tracker sticky (AC2).
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
    // Contenedor de scroll con colchón del dock; ref para condensado + data-shell-scroll del wizard.
    expect(src).toMatch(/absolute inset-0 overflow-y-auto pb-\d+/);
    expect(src).toMatch(/\{children\}/);
    expect(src).toMatch(/data-shell-scroll/);
  });
});

describe("HU #10498 — AC2: wizard con scroll en main y tracker de seguimiento", () => {
  it("StepperForm: columnas niveladas (items-stretch), sin self-start/sticky y lista con scroll propio", () => {
    const src = read("components/atom/StepperForm.tsx");
    expect(src).toMatch(/lg:items-stretch/);
    expect(src).not.toMatch(/lg:self-start/);
    expect(src).not.toMatch(/lg:sticky/);
    // La lista de pasos (ol) es la única con overflow-y-auto.
    expect(src).toMatch(/<ol className="[^"]*overflow-y-auto[^"]*">/);
  });

  it("TramiteWizard: scroll en main; chrome sticky (título + tracker)", () => {
    const wizard = read("components/operacion/TramiteWizard.tsx");
    const tracker = read("components/operacion/WizardStepTracker.tsx");
    expect(wizard).toMatch(/WizardStepTracker/);
    expect(wizard).toMatch(/tramite-wizard-scroll/);
    expect(wizard).not.toMatch(/overflow-y-auto overscroll-contain/);
    // El título del chrome. Ya no se busca el literal "Nuevo Trámite": la propuesta titula con el
    // trámite ("Matrícula Inicial"), y la referencia del expediente bajó a su propia franja.
    expect(wizard).toMatch(/\{displayTitle\}/);
    expect(wizard).toMatch(/sticky top-0/);
    expect(tracker).toMatch(/aria-label="Asistente de seguimiento"/);
    expect(tracker).not.toMatch(/sticky top/);
    expect(tracker).toMatch(/ol className="flex w-full min-w-0 items-start/);
    // Etiquetas siempre visibles (sin colapso hover).
    expect(tracker).not.toMatch(/grid-rows-\[0fr\]/);
    expect(tracker).not.toMatch(/group\/tracker/);
  });

  it("layout trámites inmersivo no clipa el scroll del Shell", () => {
    const src = read("app/tramites/layout.tsx");
    expect(src).not.toMatch(/absolute inset-0[\s\S]*overflow-hidden/);
    expect(src).toMatch(/min-h-full flex-col gap-3/);
  });
});
