import { describe, expect, it } from "vitest";
import { avisoPorDefecto, motivoSinAviso, opcionesAviso, opcionesMasivo, resumirMasivo } from "@/lib/avisos";

const BASE = { activo: true, pagado: false, notificadoFisicamente: false, contraventor: { correo: "juan@ejemplo.com" } };

describe("avisos manuales", () => {
  it("propone el aviso más avanzado de los que proceden", () => {
    expect(avisoPorDefecto(["Aviso1"])).toBe("Aviso1");
    expect(avisoPorDefecto(["Aviso1", "Aviso2"])).toBe("Aviso2");
    expect(avisoPorDefecto(["Aviso3"])).toBe("Aviso3");
    expect(avisoPorDefecto([])).toBeNull();
  });

  it("solo ofrece los avisos que proceden y marca el reenvío", () => {
    // Bug #14270 (AV-03): con el aviso 1 enviado y el 50 % vigente no se ofrecen el 2 ni el 3.
    expect(opcionesAviso(["Aviso1"], "Aviso1")).toEqual([{ valor: "Aviso1", etiqueta: "Aviso 1 (inicial): reenviar" }]);
    expect(opcionesAviso(["Aviso1", "Aviso2"], "Aviso1").map((o) => o.valor)).toEqual(["Aviso1", "Aviso2"]);
    expect(opcionesAviso(["Aviso1"], "SinNotificar")[0]!.etiqueta).toBe("Aviso 1 (inicial)");
  });

  it("en el envío masivo ofrece los que proceden en algún comparendo", () => {
    const o = opcionesMasivo(
      [
        { tipo: "Aviso1", aplica: 2 },
        { tipo: "Aviso2", aplica: 1 },
        { tipo: "Aviso3", aplica: 0 },
      ],
      3,
    );
    expect(o.map((x) => x.valor)).toEqual(["Aviso1", "Aviso2"]);
    expect(o[1]!.etiqueta).toBe("Aviso 2 (recordatorio) (procede en 1 de 3)");
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
