import { describe, expect, it } from "vitest";
import {
  ESTADOS_SANEAMIENTO,
  ROTULO_FUENTE,
  ROTULO_HALLAZGO,
  ROTULO_MOTIVO_NO_ELEGIBLE,
  ROTULO_RESULTADO,
  etiquetaSaneamiento,
  rotuloConcepto,
} from "@/lib/saneamiento/estados";

// Uso de ejemplo: etiquetaSaneamiento("EN_GESTION") → { texto: "En saneamiento", tono: "warning" };
// etiquetaSaneamiento("ANULADO") → null (cliente) · etiquetaSaneamiento("ANULADO", { backoffice: true }) → «Anulado» (D-06).

describe("lib/saneamiento/estados — etiquetas 7.1 del plan (spec-ui §1.1)", () => {
  it("cada estado no anulado tiene su texto y tono", () => {
    expect(etiquetaSaneamiento("SOLICITADO")).toEqual({ texto: "Saneamiento solicitado", tono: "info" });
    expect(etiquetaSaneamiento("EN_GESTION")).toEqual({ texto: "En saneamiento", tono: "warning" });
    expect(etiquetaSaneamiento("SANEADO")).toEqual({ texto: "Saneado", tono: "success" });
    expect(etiquetaSaneamiento("NO_PROCEDE")).toEqual({ texto: "Saneamiento no procede", tono: "neutral" });
  });

  it("sin saneamiento o ANULADO no hay etiqueta para el cliente", () => {
    expect(etiquetaSaneamiento(null)).toBeNull();
    expect(etiquetaSaneamiento(undefined)).toBeNull();
    expect(etiquetaSaneamiento("ANULADO")).toBeNull();
  });

  it("D-06: en el backoffice ANULADO se rotula «Anulado», tono neutral", () => {
    expect(etiquetaSaneamiento("ANULADO", { backoffice: true })).toEqual({ texto: "Anulado", tono: "neutral" });
    expect(etiquetaSaneamiento(null, { backoffice: true })).toBeNull();
  });

  it("un estado desconocido no se pinta (nunca el código)", () => {
    expect(etiquetaSaneamiento("OTRO_ESTADO" as never)).toBeNull();
  });

  it("los cinco estados del contrato, en orden de la bandeja", () => {
    expect(ESTADOS_SANEAMIENTO).toEqual(["SOLICITADO", "EN_GESTION", "SANEADO", "NO_PROCEDE", "ANULADO"]);
  });

  it("rótulos literales de fuentes, hallazgos, resultados y motivos", () => {
    expect(ROTULO_FUENTE).toEqual({
      SIMIT_VEHICULO: "SIMIT del vehículo",
      SIMIT_PROPIETARIO: "SIMIT del propietario",
      SIMIT_LOCATARIO: "SIMIT del locatario",
      IMPUESTO: "Impuesto vehicular",
      SOAT: "SOAT",
      RTM: "Revisión técnico-mecánica",
      OTRO: "Otro",
    });
    expect(ROTULO_HALLAZGO).toEqual({ DEUDA: "Con deuda", NO_VIGENTE: "No vigente", SIN_VERIFICAR: "Sin verificar" });
    expect(ROTULO_RESULTADO).toEqual({ PENDIENTE: "Pendiente", SANEADO: "Saneado", NO_PROCEDE: "No procede" });
    expect(ROTULO_MOTIVO_NO_ELEGIBLE).toEqual({ NO_TERMINADO: "Faltan consultas por terminar", YA_SOLICITADO: "Ya tiene un saneamiento" });
  });

  it("concepto de un renglón: la fuente, o «Otro: {descripción}»", () => {
    expect(rotuloConcepto({ fuente: "SIMIT_PROPIETARIO", descripcion: null })).toBe("SIMIT del propietario");
    expect(rotuloConcepto({ fuente: "OTRO", descripcion: "Embargo" })).toBe("Otro: Embargo");
    expect(rotuloConcepto({ fuente: "OTRO", descripcion: null })).toBe("Otro");
  });
});
