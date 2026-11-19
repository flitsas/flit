// Responsive iOS (S1, ronda 2 · decisión de Samuel): en el layout `app`, por debajo de lg el pie baja con el contenido
// (al final de `[data-shell-scroll]`) y desde lg queda fijo abajo como siempre, con su alto reservado en el contenedor.
// jsdom no aplica media queries: se comprueba el marcado (un solo nodo, dónde vive y sus clases por corte) y que el
// alto medido llegue a la variable CSS.
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { Box } from "lucide-react";
import { SuiteShell } from "../SuiteShell";

vi.mock("next/navigation", () => ({ usePathname: () => "/tramites" }));
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const nav = {
  sections: [{ id: "operacion", label: "Operación", icon: Box, side: "left" as const }],
  items: [{ key: "tramites", label: "Trámites", href: "/tramites", section: "operacion", icon: Box }],
};
const user = { email: "ana@empresa.co", tenantName: "Empresa Uno", permissions: [], isSuperAdmin: false };
const pie = <footer>Políticas de Privacidad</footer>;

describe("pie del layout app", () => {
  it("vive una sola vez al final del scroll, después del contenido, y desde lg pasa a fijo abajo", () => {
    const { container } = render(
      <SuiteShell productCode="tramites" productName="Trámites" nav={nav} user={user} layout="app" footer={pie}>
        <p>contenido</p>
      </SuiteShell>,
    );
    expect(screen.getAllByText("Políticas de Privacidad")).toHaveLength(1);
    const scroll = container.querySelector("[data-shell-scroll]") as HTMLElement;
    const envoltura = container.querySelector("[data-shell-pie]") as HTMLElement;
    expect(scroll.contains(envoltura)).toBe(true);
    expect(scroll.lastElementChild).toBe(envoltura);
    expect(envoltura.className).toContain("lg:fixed");
    expect(envoltura.className).toContain("max-lg:mt-auto");
    expect(scroll.className).toContain("max-lg:flex");
    // El contenedor reserva el alto del pie desde lg, con 2rem (el pie de Trámites) mientras no se mide.
    expect(scroll.closest(".h-dvh")?.className).toContain("lg:pb-[var(--suite-pie-alto,2rem)]");
  });

  it("sin pie no hay envoltura ni reserva (Comparendos, Diagnóstico y Hub quedan como antes)", () => {
    const { container } = render(
      <SuiteShell productCode="hub" productName="Hub" nav={nav} user={user} layout="app">
        <p>contenido</p>
      </SuiteShell>,
    );
    expect(container.querySelector("[data-shell-pie]")).toBeNull();
    const scroll = container.querySelector("[data-shell-scroll]") as HTMLElement;
    expect(scroll.className).not.toContain("max-lg:flex");
    expect(scroll.closest(".h-dvh")?.className).not.toContain("suite-pie-alto");
  });

  it("el alto medido del pie llega a la variable del contenedor", () => {
    let avisar: () => void = () => {};
    vi.stubGlobal(
      "ResizeObserver",
      class {
        constructor(cb: () => void) {
          avisar = cb;
        }
        observe() {}
        disconnect() {}
      },
    );
    const { container } = render(
      <SuiteShell productCode="tramites" productName="Trámites" nav={nav} user={user} layout="app" footer={pie}>
        <p>contenido</p>
      </SuiteShell>,
    );
    const envoltura = container.querySelector("[data-shell-pie]") as HTMLElement;
    Object.defineProperty(envoltura, "offsetHeight", { configurable: true, value: 62 });
    avisar();
    const contenedor = container.querySelector(".h-dvh") as HTMLElement;
    expect(contenedor.style.getPropertyValue("--suite-pie-alto")).toBe("62px");
  });
});
