import { describe, expect, it } from "vitest";
import { infraccionConDescripcion, mensajeMotivo, razonLegible } from "@/lib/decisionDp";

describe("panel «Qué se puede alegar» (HU #14613)", () => {
  it("AC: cada motivo se explica en palabras de la gestora, nunca con el nombre del enum", () => {
    for (const m of ["Inactivo", "Pagado", "YaTieneDp", "VentanaDescuento", "SinTipologiaAplicable", "SinTipologiaDisponible", "Inventado"]) {
      const x = mensajeMotivo(m)!;
      expect(x.titulo).not.toContain(m === "Inventado" ? "Inventado" : m);
      expect(`${x.titulo} ${x.detalle}`).not.toMatch(/Sin[A-Z]|YaTiene|Ventana[A-Z]/);
    }
    expect(mensajeMotivo("VentanaDescuento")!.titulo).toBe("Esperar: se puede trasladar al conductor con descuento");
    expect(mensajeMotivo("SinTipologiaAplicable")!.titulo).toBe("Todavía no aplica ningún argumento");
    expect(mensajeMotivo(null)).toBeNull();
  });

  it("el código de infracción de la razón sale con su descripción, una sola vez", () => {
    const r = "C35 impuesta el 2021-05-04, dentro del periodo de la sentencia C-321 de 2022.";
    const t = razonLegible(r, { codigo: "C35", descripcion: "No portar el SOAT." });
    expect(t).toBe("C35 (No portar el SOAT) impuesta el 04 de may de 2021, dentro del periodo de la sentencia C-321 de 2022.");
    expect(razonLegible(t, { codigo: "C35", descripcion: "No portar el SOAT." })).toBe(t);
  });

  it("sin descripción deja el código y formatea las fechas", () => {
    expect(razonLegible("Pasaron 3 años o más desde la notificación (2020-01-06).", null)).toContain("06 de ene de 2020");
    expect(infraccionConDescripcion("C29", "Exceso de velocidad")).toBe("C29 — Exceso de velocidad");
    expect(infraccionConDescripcion("C29", null)).toBe("C29");
    expect(infraccionConDescripcion(null, "x")).toBeNull();
  });
});
