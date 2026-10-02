import { describe, expect, it } from "vitest";
import { comingSoonProduct } from "../coming-soon";

describe("comingSoonProduct", () => {
  it("presenta Comparendos y Diagnóstico", () => {
    expect(comingSoonProduct("comparendos")?.name).toBe("Comparendos");
    expect(comingSoonProduct("diagnostico")?.name).toBe("Diagnóstico");
  });

  it("un código que no es de la lista (o una propiedad heredada) no tiene página", () => {
    expect(comingSoonProduct("tramites")).toBeNull();
    expect(comingSoonProduct("toString")).toBeNull();
  });
});
