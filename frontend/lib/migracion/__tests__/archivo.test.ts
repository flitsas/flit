import { describe, expect, it } from "vitest";
import { contenidoPlantilla, parsearCsv, validarHoja } from "@/lib/migracion/archivo";

/**
 * La lectura del archivo es donde una consola de migración masiva se juega la confianza: si acepta
 * una fila torcida y la convierte en un id distinto, migra un trámite real de otra empresa. Por eso
 * los casos de abajo se centran en lo que NO debe pasar tanto como en lo que sí.
 */
describe("parsearCsv", () => {
  it("lee filas y columnas simples", () => {
    expect(parsearCsv("tipo,id\ntraspaso,26350")).toEqual([
      ["tipo", "id"],
      ["traspaso", "26350"],
    ]);
  });

  it("respeta las comas dentro de comillas", () => {
    expect(parsearCsv('a,"uno,dos",c')).toEqual([["a", "uno,dos", "c"]]);
  });

  it("entiende la comilla escapada del RFC 4180", () => {
    expect(parsearCsv('"dice ""hola""",x')).toEqual([['dice "hola"', "x"]]);
  });

  /** Excel en español exporta con punto y coma; sin esto, todo el archivo sería una sola columna. */
  it("detecta el punto y coma como separador", () => {
    expect(parsearCsv("tipo;id\nmatricula;7426")).toEqual([
      ["tipo", "id"],
      ["matricula", "7426"],
    ]);
  });

  it("descarta el BOM que antepone Excel", () => {
    expect(parsearCsv("﻿tipo,id")[0][0]).toBe("tipo");
  });

  it("tolera los saltos de línea de Windows", () => {
    expect(parsearCsv("a,b\r\nc,d")).toEqual([
      ["a", "b"],
      ["c", "d"],
    ]);
  });
});

describe("validarHoja", () => {
  it("acepta la plantilla que la propia consola ofrece", () => {
    const { validas, invalidas } = validarHoja(parsearCsv(contenidoPlantilla()));

    expect(invalidas).toEqual([]);
    expect(validas).toEqual([
      { fila: 4, tramite: "transfer", v1Id: 26350 },
      { fila: 5, tramite: "registration", v1Id: 7426 },
    ]);
  });

  it("reconoce sinónimos, acentos y mayúsculas del tipo", () => {
    const hoja = parsearCsv("tipo,id\nTraspaso,1\nMatrícula,2\ntransfer,3\nregistration,4");

    expect(validarHoja(hoja).validas.map((f) => f.tramite)).toEqual([
      "transfer",
      "registration",
      "transfer",
      "registration",
    ]);
  });

  it("encuentra las columnas aunque estén en otro orden o con otro nombre", () => {
    const hoja = parsearCsv("v1Id,tramite\n26350,traspaso");

    expect(validarHoja(hoja).validas).toEqual([{ fila: 2, tramite: "transfer", v1Id: 26350 }]);
  });

  it("asume el orden de la plantilla si no hay encabezado", () => {
    const hoja = parsearCsv("traspaso,26350");

    expect(validarHoja(hoja).validas).toEqual([{ fila: 1, tramite: "transfer", v1Id: 26350 }]);
  });

  it("ignora filas vacías y comentarios sin reportarlos como errores", () => {
    const hoja = parsearCsv("tipo,id\n\n# un comentario\ntraspaso,1\n");
    const { validas, invalidas } = validarHoja(hoja);

    expect(validas).toHaveLength(1);
    expect(invalidas).toEqual([]);
  });

  /** Lo que Excel le hace a una columna numérica al guardarla. */
  it.each([
    ["26350", 26350],
    ["26.350", 26350],
    ["26350.0", 26350],
    [" 26350 ", 26350],
  ])("normaliza el id «%s»", (crudo, esperado) => {
    const { validas } = validarHoja(parsearCsv(`tipo,id\ntraspaso,"${crudo}"`));

    expect(validas[0]?.v1Id).toBe(esperado);
  });

  /**
   * El caso que justifica no limpiar con una expresión perezosa: quitar "todo lo que no sea
   * dígito" convertiría 26350-1 en 263501, que es OTRO trámite y existe.
   */
  it.each(["26350-1", "26350abc", "abc", "-5", "0", "1e5", ""])(
    "rechaza el id «%s» en vez de adivinarlo",
    (crudo) => {
      const { validas, invalidas } = validarHoja(parsearCsv(`tipo,id\ntraspaso,"${crudo}"`));

      expect(validas).toEqual([]);
      expect(invalidas).toHaveLength(1);
    },
  );

  it("señala el tipo desconocido y sigue con el resto del archivo", () => {
    const hoja = parsearCsv("tipo,id\ncancelacion,1\ntraspaso,2");
    const { validas, invalidas } = validarHoja(hoja);

    expect(validas).toEqual([{ fila: 3, tramite: "transfer", v1Id: 2 }]);
    expect(invalidas[0].fila).toBe(2);
    expect(invalidas[0].motivo).toContain("cancelacion");
  });

  it("marca los repetidos y dice en qué fila venía el original", () => {
    const hoja = parsearCsv("tipo,id\ntraspaso,26350\ntraspaso,26350");
    const { validas, invalidas } = validarHoja(hoja);

    expect(validas).toHaveLength(1);
    expect(invalidas[0].motivo).toContain("fila 2");
  });

  /**
   * El mismo id en tipos distintos NO es un duplicado: son tablas distintas de V1 y hay 12.807 ids
   * que existen en las dos. Tratarlos como repetidos dejaría trámites reales sin migrar.
   */
  it("no confunde el mismo id en tipos distintos con un repetido", () => {
    const hoja = parsearCsv("tipo,id\ntraspaso,26350\nmatricula,26350");

    expect(validarHoja(hoja).validas).toHaveLength(2);
    expect(validarHoja(hoja).invalidas).toEqual([]);
  });

  it("numera las filas como las ve quien abre el archivo", () => {
    const hoja = parsearCsv("tipo,id\ntraspaso,1\nbasura,2");

    expect(validarHoja(hoja).invalidas[0].fila).toBe(3);
  });
});
