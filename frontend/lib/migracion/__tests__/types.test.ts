import { describe, expect, it } from "vitest";
import { claveFila, enlaceTramite, etiquetaConteo, etiquetaTramite } from "@/lib/migracion/types";

/**
 * Lo que el host devuelve DE VERDAD, comprobado contra el migrador local apuntando a la copia de
 * producción. Los valores de abajo están copiados de una respuesta real, no inventados: la primera
 * versión de `etiquetaTramite` mapeaba el slug de la URL (`transfer`) y el host manda el nombre
 * para el operador (`traspaso`), así que el reporte acababa mostrando «traspaso #26350» en
 * minúscula junto a una tabla que decía «Traspaso».
 */
describe("etiquetaTramite", () => {
  it.each([
    ["traspaso", "Traspaso"],
    ["matrícula inicial", "Matrícula inicial"],
  ])("pone en mayúscula el nombre que manda el host: %s", (crudo, esperado) => {
    expect(etiquetaTramite(crudo)).toBe(esperado);
  });

  /** Por si alguna respuesta trae el `CliName` en vez del nombre. */
  it.each([
    ["transfer", "Traspaso"],
    ["registration", "Matrícula"],
  ])("también entiende el slug: %s", (crudo, esperado) => {
    expect(etiquetaTramite(crudo)).toBe(esperado);
  });

  it("no revienta con vacío", () => {
    expect(etiquetaTramite("")).toBe("");
  });
});

describe("etiquetaConteo", () => {
  /** Los tres contadores que devolvió la instancia de datos en la corrida real. */
  it.each([
    ["campos", "Campos"],
    ["actores", "Actores"],
    ["eventosHistorial", "Eventos de historial"],
  ])("traduce %s", (clave, esperado) => {
    expect(etiquetaConteo(clave)).toBe(esperado);
  });

  /** Un contador que el motor añada mañana debe leerse, no dejar un hueco. */
  it("parte el camelCase de lo que no conoce", () => {
    expect(etiquetaConteo("piezasNuevasDelMotor")).toBe("Piezas Nuevas Del Motor");
  });
});

describe("claveFila", () => {
  /**
   * Un traspaso y una matrícula pueden compartir id de V1 —viven en tablas distintas y hay 12.807
   * ids en las dos—, así que la clave nunca puede ser el id solo.
   */
  it("distingue el mismo id entre tipos", () => {
    expect(claveFila({ tramite: "transfer", v1Id: 26350 })).not.toBe(
      claveFila({ tramite: "registration", v1Id: 26350 }),
    );
  });
});

describe("enlaceTramite", () => {
  /** El tenant es obligatorio: sin `?t=` el trámite no resuelve y la pantalla queda vacía. */
  it("lleva el tenant en la query", () => {
    expect(
      enlaceTramite({
        v2Id: "c1fc97dc-90ec-54c7-9121-8e6d9dbe24ea",
        tenantId: "51019fc4-bfe4-4a03-9197-eb6e0c5d95d0",
      }),
    ).toBe(
      "/tramites/c1fc97dc-90ec-54c7-9121-8e6d9dbe24ea?t=51019fc4-bfe4-4a03-9197-eb6e0c5d95d0",
    );
  });
});
