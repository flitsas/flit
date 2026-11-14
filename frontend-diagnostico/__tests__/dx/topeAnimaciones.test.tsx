import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { PantallaResultado } from "@flit/ui/PantallaResultado";
import { CampanaDiagnostico } from "@/components/dx/campana/CampanaDiagnostico";
import { EstadoGlobal } from "@/components/tablero/EstadoGlobal";
import { grilla } from "@/lib/tablero/__tests__/datos";
import { aviso, bandeja, bandejas, simularApi } from "@/lib/notificaciones/__tests__/datos";

// Uso de ejemplo: animacionesContinuas(document.body) devuelve los elementos con una clase `animate-*` (sin contar
// `animate-none`). HU #14562 AC5 [CF-04]: cada pantalla de Diagnóstico tiene como máximo 2 animaciones continuas y
// toda `animate-*` va condicionada (`motion-safe:animate-*`) o se apaga con `motion-reduce:animate-none`.
// Se mira document.body y no solo `container` porque el panel de la campana se pinta en un portal.

const TOPE_AC5 = 2;

const clasesAnimacion = (el: Element) =>
  (el.getAttribute("class") ?? "").split(/\s+/).filter((c) => /(^|:)animate-(?!none$)/.test(c));

function animacionesContinuas(raiz: Element) {
  return Array.from(raiz.querySelectorAll("[class]")).filter((el) => clasesAnimacion(el).length > 0);
}

function sinVarianteReducida(raiz: Element) {
  return animacionesContinuas(raiz).filter((el) => {
    const clases = (el.getAttribute("class") ?? "").split(/\s+/);
    const condicionadas = clasesAnimacion(el).every((c) => c.startsWith("motion-safe:"));
    return !condicionadas && !clases.includes("motion-reduce:animate-none");
  });
}

function verificar(raiz: Element) {
  const conteo = animacionesContinuas(raiz).length;
  expect(conteo).toBeLessThanOrEqual(TOPE_AC5);
  expect(sinVarianteReducida(raiz).map((el) => el.getAttribute("class"))).toEqual([]);
  return conteo;
}

afterEach(() => vi.unstubAllGlobals());

describe("HU14562 AC5 tope de animaciones continuas por pantalla", () => {
  it("tablero en curso (EstadoGlobal EN_PROCESO, sondeando): ≤ 2 y todas con variante de movimiento reducido", () => {
    render(<EstadoGlobal grilla={grilla({ estado: "EN_PROCESO" })} sondeando />);
    expect(verificar(document.body)).toBe(1);
  });

  it("campana con avisos sin leer y el panel abierto: ≤ 2 y todas con variante de movimiento reducido", async () => {
    simularApi({ "GET /notificaciones": bandejas(bandeja([aviso(1), aviso(2)])) });
    render(<CampanaDiagnostico />);
    fireEvent.click(await screen.findByRole("button", { name: /^Avisos/ }));
    await screen.findByRole("region", { name: "Avisos" });
    expect(verificar(document.body)).toBe(0);
  });

  it("PantallaResultado con redirección: ≤ 2 y todas con variante de movimiento reducido", () => {
    render(
      <PantallaResultado
        tono="exito"
        titulo="Diagnóstico iniciado con 3 vehículos"
        mensaje="Las consultas ya arrancaron."
        primaria={{ etiqueta: "Ver tablero", onClick: () => {} }}
        secundaria={{ etiqueta: "Volver", href: "/" }}
        redireccion={{ segundos: 5, destino: "el tablero", onNavegar: () => {} }}
      />,
    );
    expect(verificar(document.body)).toBe(0);
  });

  it("el detector reconoce una animación sin variante reducida (control del propio test)", () => {
    const { container } = render(
      <div>
        <span className="animate-spin" />
        <span className="motion-safe:animate-pulse" />
        <span className="animate-ping motion-reduce:animate-none" />
      </div>,
    );
    expect(animacionesContinuas(container)).toHaveLength(3);
    expect(sinVarianteReducida(container).map((el) => el.getAttribute("class"))).toEqual(["animate-spin"]);
  });
});
