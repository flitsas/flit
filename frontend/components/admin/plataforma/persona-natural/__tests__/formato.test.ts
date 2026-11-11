import { describe, expect, it } from "vitest";
import { nombrePropio } from "../formato";

describe("nombrePropio", () => {
  it("pasa a formato título y deja los conectores en minúscula", () => {
    expect(nombrePropio("SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA")).toBe("Secretaria Distrital de Movilidad de Bogota");
    expect(nombrePropio("INSTITUTO DEL TRANSITO Y TRANSPORTE")).toBe("Instituto del Transito y Transporte");
  });
  it("conserva tildes, siglas sin vocales y puntos", () => {
    expect(nombrePropio("TRÁNSITO DE MEDELLÍN")).toBe("Tránsito de Medellín");
    expect(nombrePropio("TTO MUNICIPAL")).toBe("TTO Municipal");
    expect(nombrePropio("BOGOTA D.C.")).toBe("Bogota D.C.");
  });
  it("no toca las palabras que ya traen minúsculas (siglas mixtas o datos bien escritos)", () => {
    expect(nombrePropio("STRIA TTEyTTO ENVIGADO")).toBe("Stria TTEyTTO Envigado");
    expect(nombrePropio("Secretaría de Movilidad de Bogotá")).toBe("Secretaría de Movilidad de Bogotá");
  });
  it("el primer término nunca queda como conector y los vacíos devuelven cadena vacía", () => {
    expect(nombrePropio("DE LA PAZ")).toBe("De la Paz");
    expect(nombrePropio("")).toBe("");
    expect(nombrePropio(null)).toBe("");
  });
});
