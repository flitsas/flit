import { describe, expect, it } from "vitest";
import { clicFueraDelDialogo } from "@/lib/ui/clic-dialogo";

function evento(clientX: number, clientY: number, cuadro: { left: number; top: number; right: number; bottom: number }) {
  return { currentTarget: { getBoundingClientRect: () => cuadro } as unknown as HTMLElement, clientX, clientY };
}

const caja = { left: 100, top: 80, right: 500, bottom: 400 };

describe("clic dentro del modal", () => {
  it("el relleno y el centro de la caja no cuentan como clic fuera", () => {
    expect(clicFueraDelDialogo(evento(120, 100, caja))).toBe(false);
    expect(clicFueraDelDialogo(evento(300, 240, caja))).toBe(false);
    expect(clicFueraDelDialogo(evento(100, 80, caja))).toBe(false);
  });

  it("el fondo, alrededor de la caja, sí cierra", () => {
    expect(clicFueraDelDialogo(evento(10, 200, caja))).toBe(true);
    expect(clicFueraDelDialogo(evento(300, 10, caja))).toBe(true);
  });
});
