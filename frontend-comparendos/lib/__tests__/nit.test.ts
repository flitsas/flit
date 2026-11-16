import { describe, expect, it } from "vitest";
import { digitoVerificacion, errorNit, mascaraNit, nitValido, separarNit } from "../nit";

describe("nit", () => {
  it("separa base y DV con formato comercial", () => {
    expect(separarNit("900.123.456-7")).toEqual({ base: "900123456", dv: 7 });
    expect(separarNit("900123456")).toEqual({ base: "900123456", dv: null });
  });

  it("calcula el dígito DIAN", () => {
    expect(digitoVerificacion("900123456")).toBe(digitoVerificacion("900123456"));
    expect(nitValido(`900123456-${digitoVerificacion("900123456")}`)).toBe(true);
  });

  it("rechaza DV incorrecto y acepta sin DV", () => {
    const dv = digitoVerificacion("900123456");
    const malo = (dv + 1) % 10;
    expect(errorNit(`900123456-${malo}`)).toContain("debería ser");
    expect(errorNit("900123456")).toBeNull();
    expect(errorNit("ABC")).toContain("formato");
  });

  it("GDC punto 6: el caso de QA 890903938-1 avisa que el dígito es 8", () => {
    expect(digitoVerificacion("890903938")).toBe(8);
    expect(errorNit("890903938-1")).toContain("debería ser 8");
    expect(errorNit("890.903.938-8")).toBeNull();
  });

  it("GDC punto 6: enmascara mientras se escribe", () => {
    expect(mascaraNit("9")).toBe("9");
    expect(mascaraNit("9001")).toBe("9.001");
    expect(mascaraNit("900123456")).toBe("900.123.456");
    expect(mascaraNit("900123456-")).toBe("900.123.456-");
    expect(mascaraNit("900123456-7")).toBe("900.123.456-7");
    expect(mascaraNit("900.123.456-78")).toBe("900.123.456-7");
    expect(mascaraNit("90a0 12x3")).toBe("900.123");
    expect(mascaraNit("-")).toBe("");
    expect(mascaraNit("")).toBe("");
    // Lo enmascarado se sigue leyendo igual que lo digitado.
    expect(separarNit(mascaraNit("8909039388"))).toEqual({ base: "8909039388", dv: null });
  });
});
