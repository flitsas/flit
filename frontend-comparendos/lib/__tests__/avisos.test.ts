import { describe, expect, it } from "vitest";
import { motivoSinAviso, resumirMasivo, siguienteAviso } from "@/lib/avisos";

const BASE = { activo: true, pagado: false, notificadoFisicamente: false, contraventor: { correo: "juan@ejemplo.com" } };

describe("avisos manuales", () => {
  it("sugiere el siguiente aviso según el estado del conductor", () => {
    expect(siguienteAviso("SinNotificar")).toBe("Aviso1");
    expect(siguienteAviso("Aviso1")).toBe("Aviso2");
    expect(siguienteAviso("Aviso2")).toBe("Aviso3");
    expect(siguienteAviso("Aviso3")).toBe("Aviso3");
    expect(siguienteAviso(null)).toBe("Aviso1");
  });

  it("explica por qué no se puede enviar", () => {
    expect(motivoSinAviso(BASE)).toBeNull();
    expect(motivoSinAviso({ ...BASE, pagado: true })).toMatch(/pagado/);
    expect(motivoSinAviso({ ...BASE, contraventor: null })).toMatch(/conductor/);
    expect(motivoSinAviso({ ...BASE, contraventor: { correo: null } })).toMatch(/correo/);
    expect(motivoSinAviso({ ...BASE, activo: false })).toMatch(/inactivo/);
  });

  it("resume el envío masivo", () => {
    const r = resumirMasivo([
      { comparendoId: "1", numero: "A-1", enviado: true, motivo: null },
      { comparendoId: "2", numero: "B-2", enviado: false, motivo: "Ya recibió el aviso 1." },
      { comparendoId: "3", enviado: false, motivo: null },
    ]);
    expect(r.enviados).toBe(1);
    expect(r.omitidos).toEqual([
      { numero: "B-2", motivo: "Ya recibió el aviso 1." },
      { numero: "3", motivo: "Sin motivo" },
    ]);
  });
});
