// Responsive iOS (S1): las capas de la suite fijan la página de atrás mientras están abiertas (`useBloqueoScroll` de F0).
// En iOS, Safari y Chrome (ambos WebKit) mueven la página de fondo al arrastrar dentro de una capa si solo hay
// `overflow: hidden`; el hook fija `body`. Aquí se comprueba que la hoja del dock, el Modal y el PanelLateral lo usan
// y que al cerrar la página queda como antes. El foco y Escape no cambian (los cubren sus propias pruebas).
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { Box } from "lucide-react";
import { Modal } from "@flit/ui/Modal";
import { PanelLateral } from "@flit/ui/PanelLateral";
import { DockMobile } from "../dock/DockMobile";
import type { DockGroup } from "../nav";

vi.mock("next/navigation", () => ({ usePathname: () => "/" }));
afterEach(cleanup);

const grupos: DockGroup[] = [
  {
    id: "operacion",
    label: "Operación",
    icon: Box,
    side: "left",
    active: false,
    items: [{ key: "a", label: "Trámites", href: "/tramites", icon: Box, active: false }],
  },
];

describe("bloqueo de scroll en las capas", () => {
  it("la hoja del dock fija la página al abrir y la suelta al cerrar", () => {
    render(<DockMobile groups={grupos} homeIconSrc="/favicon.svg" />);
    expect(document.body.style.position).toBe("");
    fireEvent.click(screen.getByRole("button", { name: "Abrir menú de navegación" }));
    expect(document.body.style.position).toBe("fixed");
    fireEvent.click(screen.getByRole("button", { name: "Cerrar menú" }));
    expect(document.body.style.position).toBe("");
  });

  it("el Modal fija la página mientras está abierto y su cuerpo no encadena el scroll", () => {
    const { rerender } = render(
      <Modal open onClose={() => {}} title="Detalle">
        <p>Contenido</p>
      </Modal>,
    );
    expect(document.body.style.position).toBe("fixed");
    expect(screen.getByText("Contenido").parentElement?.className).toContain("overscroll-contain");
    rerender(
      <Modal open={false} onClose={() => {}} title="Detalle">
        <p>Contenido</p>
      </Modal>,
    );
    expect(document.body.style.position).toBe("");
  });

  it("el PanelLateral fija la página y deja el pie sobre la barra de inicio del iPhone", () => {
    render(
      <PanelLateral abierto onCerrar={() => {}} titulo="Dictamen" pie={<button type="button">Guardar</button>}>
        <p>Detalle</p>
      </PanelLateral>,
    );
    expect(document.body.style.position).toBe("fixed");
    expect(screen.getByText("Detalle").parentElement?.className).toContain("overscroll-contain");
    expect(screen.getByRole("button", { name: "Guardar" }).parentElement?.className).toContain("safe-area-inset-bottom");
    cleanup();
    expect(document.body.style.position).toBe("");
  });
});
