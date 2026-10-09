import { describe, expect, it } from "vitest";
import { errorRango, etiquetaMes, formatoTasa, inicioDeMes, numeroCorto, topeEje, totalesProductividad, valorSerie, type PuntoMensual } from "@/lib/reportes";

const punto: PuntoMensual = { periodo: "2026-10", ingresados: 4, valorIngresado: 100, dpEnviados: 2, dpFavorables: 1, ahorro: 50, pagosCc: 1, pagosPc: 2, pagosBc: 3 };

describe("reportes", () => {
  it("formatea la tasa con coma y sin respuestas muestra raya", () => {
    expect(formatoTasa(0.5)).toBe("50 %");
    expect(formatoTasa(2 / 3)).toBe("66,7 %");
    expect(formatoTasa(null)).toBe("—");
  });

  it("suma los pagos por tipo y trata un mes vacío como cero", () => {
    expect(valorSerie(punto, "pagos")).toBe(6);
    expect(valorSerie(punto, "ahorro")).toBe(50);
    expect(valorSerie(null, "ingresados")).toBe(0);
  });

  it("redondea el tope del eje y acorta números", () => {
    expect(topeEje(0)).toBe(1);
    expect(topeEje(7)).toBe(10);
    expect(topeEje(1600000)).toBe(2000000);
    expect(topeEje(230)).toBe(250);
    expect(numeroCorto(1604100)).toBe("1,6 M");
    expect(numeroCorto(12000)).toBe("12 mil");
    expect(numeroCorto(7)).toBe("7");
  });

  it("etiqueta meses y calcula el inicio de mes", () => {
    expect(etiquetaMes("2026-10")).toBe("oct 26");
    expect(etiquetaMes("2026-01", false)).toBe("ene");
    expect(inicioDeMes("2026-10-09")).toBe("2026-10-01");
  });

  it("valida el rango como la API", () => {
    expect(errorRango("2026-10-01", "2026-10-09")).toBeNull();
    expect(errorRango("2026-10-10", "2026-10-09")).toMatch(/posterior/);
    expect(errorRango("2020-01-01", "2026-10-09")).toMatch(/tres años/);
    expect(errorRango("", "2026-10-09")).toMatch(/dos fechas/);
  });

  it("totaliza la productividad por métrica", () => {
    const base = { usuarioId: "u", usuario: "A", gestionados: 4, dpEnviados: 2, contraventoresAsignados: 1, pagos: 1, avisosManuales: 0, ocrRegistrados: 0, respuestasOt: 2 };
    const t = totalesProductividad([base, { ...base, usuarioId: "v", gestionados: 1 }]);
    expect(t.gestionados).toBe(5);
    expect(t.respuestasOt).toBe(4);
  });
});
