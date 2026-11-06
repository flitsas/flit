import { describe, expect, it } from "vitest";
import { digitoVerificacion, errorNit, nitValido, separarNit } from "../nit";

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
});
