import { describe, expect, it } from "vitest";
import { filasDiagnostico, motivosDiagnostico } from "../diagnostico";

const DUPLICIDAD = "Esta placa ya tiene un trámite en proceso. No puedes iniciar otro hasta que ese trámite termine o se anule. Puedes ver su estado en «Consultar mi trámite».";

describe("motivosDiagnostico", () => {
  it("bloqueado: devuelve el texto del servidor tal cual, sin los requisitos que cumplen", () => {
    const motivos = motivosDiagnostico({ puedeContinuar: false, requisitos: [{ codigo: "soat", descripcion: "SOAT", estado: "cumple" }, { codigo: "duplicidad", descripcion: DUPLICIDAD, estado: "falta" }] });
    expect(motivos).toEqual([DUPLICIDAD]);
  });
  it("sin bloqueo o sin diagnóstico no hay motivos", () => {
    expect(motivosDiagnostico(null)).toEqual([]);
    expect(motivosDiagnostico({ puedeContinuar: true, requisitos: [{ codigo: "x", descripcion: "y", estado: "falta" }] })).toEqual([]);
  });
});

describe("filasDiagnostico con el texto largo de duplicidad", () => {
  it("la cinta lleva «Trámite en proceso», no el párrafo", () => {
    const filas = filasDiagnostico([{ codigo: "duplicidad", descripcion: DUPLICIDAD, estado: "falta" }, { codigo: "soat", descripcion: "SOAT", estado: "cumple" }]);
    expect(filas.map((f) => f.nombre)).toEqual(["Trámite en proceso", "SOAT"]);
  });
});
