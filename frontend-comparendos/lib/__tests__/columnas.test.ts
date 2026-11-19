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

describe("columnas que vienen ocultas (HU #13871)", () => {
  const VALIDAS = ["numero", "placa", "valor", "ventana", "activo"];
  const DEFECTO = ["ventana", "activo"];

  it("AC4: sin nada guardado, o con algo dañado, solo las ocultas por defecto", () => {
    expect(leerOcultas(null, VALIDAS, DEFECTO)).toEqual(["ventana", "activo"]);
    expect(leerOcultas("no es json", VALIDAS, DEFECTO)).toEqual(["ventana", "activo"]);
  });

  it("AC4: una preferencia de antes (lista) no las conocía: quedan ocultas junto con lo que el usuario ocultó", () => {
    expect(leerOcultas(JSON.stringify(["placa"]), VALIDAS, DEFECTO)).toEqual(["placa", "ventana", "activo"]);
  });

  it("AC4: si el usuario ya las vio y activó una, se respeta", () => {
    const guardado = JSON.stringify({ ocultas: ["activo"], conocidas: VALIDAS });
    expect(leerOcultas(guardado, VALIDAS, DEFECTO)).toEqual(["activo"]);
  });
});
