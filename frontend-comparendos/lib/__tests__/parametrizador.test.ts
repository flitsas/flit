import { describe, expect, it } from "vitest";
import { partirCorreos } from "@/lib/correos";
import { cambiosDeAuditoria, erroresTipologia, limpiarCopias, type TipologiaForm } from "@/lib/parametrizador";

const BASE: TipologiaForm = {
  codigo: "DP-DUP",
  nombre: "Doble sanción",
  descripcionJuridica: "El mismo hecho se sancionó dos veces.",
  prefijoRadicado: "DUP",
  estadosLegalesPermitidos: ["EnTiempo"],
  permiteMasivo: false,
  plazoRespuestaOtDiasHabiles: 15,
  adjuntosRequeridos: [],
  activo: true,
};

describe("erroresTipologia", () => {
  it("acepta una tipología completa", () => expect(erroresTipologia(BASE)).toEqual([]));

  it("explica cada problema", () => {
    const errores = erroresTipologia({ ...BASE, prefijoRadicado: "DUP1", estadosLegalesPermitidos: [], plazoRespuestaOtDiasHabiles: 0 });
    expect(errores).toHaveLength(3);
  });
});

describe("cambiosDeAuditoria", () => {
  it("convierte el JSON de la auditoría en texto legible", () => {
    expect(cambiosDeAuditoria('[{"campo":"has_ocr_access","anterior":false,"nuevo":true},{"campo":"remitente_nombre","anterior":null,"nuevo":"Renting"}]')).toEqual([
      { campo: "has_ocr_access", anterior: "no", nuevo: "sí" },
      { campo: "remitente_nombre", anterior: "—", nuevo: "Renting" },
    ]);
  });

  it("tolera un JSON roto", () => expect(cambiosDeAuditoria("{")).toEqual([]));
});

describe("correos", () => {
  it("parte y valida listas de correos", () => {
    expect(partirCorreos("A@x.co, b@x.co; a@x.co\nmalo")).toEqual({ validos: ["a@x.co", "b@x.co"], invalidos: ["malo"] });
  });

  it("limpia copias vacías y repetidas", () => {
    expect(limpiarCopias([{ correo: " A@x.co ", tipos: ["DpOt", "DpOt"] }, { correo: "a@x.co", tipos: [] }, { correo: "", tipos: [] }])).toEqual([
      { correo: "a@x.co", tipos: ["DpOt"] },
    ]);
  });
});
