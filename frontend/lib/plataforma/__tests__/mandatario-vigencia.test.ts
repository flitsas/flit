// HU #13133 (Feature F2 #13114, épica #13090) — presentación de modelo y vigencia del mandatario.
import { describe, expect, it } from "vitest";
import { etiquetaModelo, modeloDe, presentarVigencia } from "@/lib/plataforma/mandatario-vigencia";

describe("HU #13133 — presentarVigencia", () => {
  it.each([
    ["vigente", "Vigente", "success"],
    ["por_vencer", "Por vencer", "warning"],
    ["vencido", "Vencido", "danger"],
    ["inactivo", "Inactivo", "neutral"],
    ["no_vigente", "Aún no vigente", "info"],
  ] as const)("AC2/AC3: %s se lee como «%s» (texto, no solo color)", (estado, texto, tone) => {
    const v = presentarVigencia({
      signerModel: "natural",
      validityStatus: estado,
      isActive: estado !== "inactivo",
    });
    expect(v).toMatchObject({ estado, texto, tone });
  });

  it("AC2: un mandatario inactivo se muestra inactivo aunque el servidor diga vigente", () => {
    expect(
      presentarVigencia({ signerModel: "natural", validityStatus: "vigente", isActive: false }).texto,
    ).toBe("Inactivo");
  });

  it("AC4: persona jurídica y formato en blanco no tienen vigencia", () => {
    expect(
      presentarVigencia({ signerModel: "juridica", validityStatus: "vigente", isActive: true }).estado,
    ).toBe("sin_vigencia");
    expect(presentarVigencia({ signerModel: "formato_blanco", isActive: true }).texto).toBe("Sin vigencia");
  });

  it("un mandatario anterior al cambio (sin modelo) es Persona natural", () => {
    expect(modeloDe({})).toBe("natural");
    expect(etiquetaModelo({})).toBe("Persona natural");
    expect(etiquetaModelo({ signerModel: "formato_blanco" })).toBe("Formato en blanco");
  });
});
