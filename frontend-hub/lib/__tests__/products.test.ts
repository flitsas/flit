import { describe, expect, it } from "vitest";
import { PRODUCTS, productInfo } from "../products";

// El catálogo de presentación: lo que dicen el landing, el inicio y «Próximamente».

describe("catálogo de productos", () => {
  it("presenta Trámites disponible y Comparendos, Diagnóstico e Impuestos próximamente, en ese orden", () => {
    expect(PRODUCTS.map((p) => [p.code, p.status])).toEqual([
      ["tramites", "available"],
      ["comparendos", "soon"],
      ["diagnostico", "soon"],
      ["impuestos", "soon"],
    ]);
  });

  // HU #14695 · M19 (spec-visual §3.11): ficha de Impuestos «Próximamente» con los textos aprobados.
  it("Impuestos: nombre, ícono de la DDL, tagline, descripción, funciones y públicos", () => {
    expect(productInfo("impuestos")).toEqual({
      code: "impuestos",
      name: "Impuestos",
      icon: "receipt",
      tagline: "Liquidación del impuesto vehicular de tus placas",
      description:
        "Liquida el impuesto vehicular de una placa o de toda tu flota con los datos del RUNT, y guarda los recibos en PDF " +
        "para pagar en el departamento que corresponda.",
      features: [
        "Liquidación por placa con datos del RUNT",
        "Liquidación masiva por lotes con avance en línea",
        "Recibos en PDF descargables",
        "Tablero con totales por compañía y rango de fechas",
        "Reportes filtrables y exportables",
      ],
      audiences: ["Flotas", "Concesionarios", "Gestores de trámites"],
      status: "soon",
    });
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
