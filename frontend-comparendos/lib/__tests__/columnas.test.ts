import { describe, expect, it } from "vitest";
import { COLUMNA_FIJA, leerOcultas, ocultasDesde } from "@/lib/columnas";

const TODAS = ["numero", "placa", "valor", "dp"];

describe("columnas configurables de la bandeja (T2)", () => {
  it("lee solo claves válidas y nunca oculta el número", () => {
    expect(leerOcultas(JSON.stringify(["placa", "numero", "inventada", 3]), TODAS)).toEqual(["placa"]);
  });

  it("con datos dañados o vacíos no oculta nada", () => {
    expect(leerOcultas("no es json", TODAS)).toEqual([]);
    expect(leerOcultas('{"placa":true}', TODAS)).toEqual([]);
    expect(leerOcultas(null, TODAS)).toEqual([]);
  });

  it("guarda las ocultas, así una columna nueva aparece visible", () => {
    expect(ocultasDesde(["placa"], TODAS)).toEqual(["valor", "dp"]);
    expect(ocultasDesde([], TODAS)).not.toContain(COLUMNA_FIJA);
  });
});
