import { describe, expect, it } from "vitest";
import { PRODUCTS, productInfo } from "../products";

// El catálogo de presentación: lo que dicen el landing, el inicio y «Próximamente».

describe("catálogo de productos", () => {
  it("presenta Trámites disponible y Comparendos y Diagnóstico próximamente, en ese orden", () => {
    expect(PRODUCTS.map((p) => [p.code, p.status])).toEqual([
      ["tramites", "available"],
      ["comparendos", "soon"],
      ["diagnostico", "soon"],
    ]);
  });

  it("cada producto trae lo que necesitan sus secciones", () => {
    for (const p of PRODUCTS) {
      expect(p.tagline.length).toBeGreaterThan(20);
      expect(p.features.length).toBeGreaterThanOrEqual(4);
      expect(p.audiences.length).toBeGreaterThan(0);
    }
  });

  it("un código que no es de la suite no tiene ficha", () => {
    expect(productInfo("plataforma")).toBeNull();
    expect(productInfo("demo")).toBeNull();
  });
});
