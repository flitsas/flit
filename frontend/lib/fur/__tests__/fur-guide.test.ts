import { describe, expect, it } from "vitest";
import { buildFurGuide } from "../fur-guide";

describe("buildFurGuide", () => {
  it("matrícula nueva sin extras: casilla 1 y sin observaciones automáticas", () => {
    const g = buildFurGuide({
      code: "MATRICULA_NUEVA",
      family: "MATRICULAS",
      prenda: "ninguna",
      color: false,
      carroceria: false,
      combustible: false,
      blindaje: false,
    });
    expect(g.casillas.map((c) => c.n)).toEqual([1]);
    expect(g.observaciones).toEqual([]);
  });

  it("suma prenda y transformaciones al tipo", () => {
    const g = buildFurGuide({
      code: "TRASPASO_STANDARD",
      family: "TRASPASO",
      prenda: "inscripcion",
      color: true,
      carroceria: true,
      combustible: false,
      blindaje: false,
    });
    expect(g.casillas.map((c) => c.n)).toEqual([2, 5, 11, 17]);
    expect(g.observaciones[0]).toContain("Inscripción de prenda a favor de");
    expect(g.observaciones).toContain("Color nuevo(NUEVO COLOR: {COLOR_NUEVO})");
    expect(g.observaciones).toContain("Carroceria nueva(NUEVA CARROCERIA: {CARROCERIA_NUEVA})");
  });

  it("leasing declara observación de locatario", () => {
    const g = buildFurGuide({
      code: "MATRICULA_LEASING",
      family: "MATRICULAS",
      prenda: "ninguna",
      color: false,
      carroceria: false,
      combustible: false,
      blindaje: false,
    });
    expect(g.casillas.map((c) => c.n)).toEqual([1]);
    expect(g.observaciones[0]).toContain("Matrícula con locatario por Leasing");
  });
});
