import { describe, expect, it } from "vitest";
import {
  CLAVE_VEHICULO,
  borradorVacio,
  construirCuerpo,
  esquemaDe,
  type AvaluoBorrador,
  type RuesBorrador,
  type RuntPersonaBorrador,
  type SimitBorrador,
} from "@/lib/backoffice/esquemasManual";
import { formulario, personaEnError } from "./datos";

// Uso de ejemplo: construirCuerpo(formulario, { vehiculo: borrador }) → { cuerpo: { version, resultado }, errores: {} } o, con
// un valor fuera del esquema, { cuerpo: null, errores: { "resultado.fuentes[0].valor": "…" } } (misma ruta que el 422 de la API).

const simit = (cambios: Partial<SimitBorrador> = {}): SimitBorrador => ({ ...(borradorVacio("SIMIT") as SimitBorrador), ...cambios });
const avaluo = (cambios: Partial<AvaluoBorrador> = {}): AvaluoBorrador => ({ ...(borradorVacio("AVALUO") as AvaluoBorrador), ...cambios });

describe("esquemaDe — un formulario por tipo de resultado", () => {
  it.each([
    ["SIMIT_VEHICULO", null, "SIMIT"],
    ["AVALUO", null, "AVALUO"],
    ["RUNT_PROPIETARIO", "RUNT_PERSONA", "RUNT_PERSONA"],
    ["RUNT_PROPIETARIO", "RUES", "RUES"],
    ["SIMIT_LOCATARIO", "SIMIT", "SIMIT"],
  ] as const)("%s (consulta %s) → %s", (fuente, consulta, tipo) => {
    expect(esquemaDe(fuente, consulta)).toBe(tipo);
  });
});

describe("construirCuerpo — SIMIT", () => {
  it("cuenta los comparendos y manda solo los campos llenos", () => {
    const b = simit({ semaforo: "AMARILLO", comparendos: [{ numero: "TST-C-0001", fecha: "2026-09-30", valor: "468500", estado: "", organismo: "" }] });
    const { cuerpo, errores } = construirCuerpo(formulario("SIMIT_VEHICULO"), { [CLAVE_VEHICULO]: b });
    expect(errores).toEqual({});
    expect(cuerpo).toEqual({
      version: 10,
      resultado: { semaforo: "AMARILLO", cantidad_comparendos: 1, comparendos: [{ numero: "TST-C-0001", fecha: "2026-09-30", valor: "468500" }] },
    });
  });

  it("número obligatorio y sin repetir; valor con formato de pesos", () => {
    const fila = { numero: "C-1", fecha: "", valor: "12,5", estado: "", organismo: "" };
    const { cuerpo, errores } = construirCuerpo(formulario("SIMIT_VEHICULO"), {
      [CLAVE_VEHICULO]: simit({ comparendos: [fila, { ...fila, valor: "" }, { ...fila, numero: "", valor: "" }] }),
    });
    expect(cuerpo).toBeNull();
    expect(Object.keys(errores).sort()).toEqual(["resultado.comparendos[0].valor", "resultado.comparendos[1].numero", "resultado.comparendos[2].numero"]);
  });
});

describe("construirCuerpo — AVALUO", () => {
  it("con valor: valor sugerido, fuente principal OK y valor de la fuente OK", () => {
    const b = avaluo({ valor_sugerido: "48500000", fuente_principal: "fasecolda", fuentes: [{ fuente: "fasecolda", estado: "OK", valor: "48500000", muestras: "3" }] });
    const { cuerpo, errores } = construirCuerpo(formulario("AVALUO"), { [CLAVE_VEHICULO]: b });
    expect(errores).toEqual({});
    expect(cuerpo).toEqual({
      version: 10,
      resultado: { sin_valor_comercial: false, valor_sugerido: 48500000, fuente_principal: "fasecolda", fuentes: [{ fuente: "fasecolda", estado: "OK", valor: 48500000, muestras: 3 }] },
    });
  });

  it("sin valor comercial: sin valor sugerido ni principal y ninguna fuente OK", () => {
    const b = avaluo({ sin_valor_comercial: true, valor_sugerido: "999", fuentes: [{ fuente: "fasecolda", estado: "SIN_DATOS", valor: "", muestras: "" }] });
    const { cuerpo } = construirCuerpo(formulario("AVALUO"), { [CLAVE_VEHICULO]: b });
    expect(cuerpo).toEqual({ version: 10, resultado: { sin_valor_comercial: true, fuentes: [{ fuente: "fasecolda", estado: "SIN_DATOS" }] } });
  });

  it("AC5: un valor fuera del esquema (texto, cero o fuente principal sin OK) da error en su campo y no hay cuerpo", () => {
    const b = avaluo({ valor_sugerido: "abc", fuente_principal: "mercado_libre", fuentes: [{ fuente: "fasecolda", estado: "OK", valor: "0", muestras: "" }] });
    const { cuerpo, errores } = construirCuerpo(formulario("AVALUO"), { [CLAVE_VEHICULO]: b });
    expect(cuerpo).toBeNull();
    expect(errores).toHaveProperty(["resultado.valor_sugerido"]);
    expect(errores).toHaveProperty(["resultado.fuentes[0].valor"]);
    expect(errores).toHaveProperty(["resultado.fuente_principal"]);
  });

  it("sin valor comercial con una fuente OK es un error de esa fuente", () => {
    const b = avaluo({ sin_valor_comercial: true, fuentes: [{ fuente: "fasecolda", estado: "OK", valor: "100", muestras: "" }] });
    const { errores } = construirCuerpo(formulario("AVALUO"), { [CLAVE_VEHICULO]: b });
    expect(errores).toHaveProperty(["resultado.fuentes[0].estado"]);
  });
});

describe("construirCuerpo — fuentes de persona", () => {
  it("una entrada por persona en error, con su persona_id y el esquema de su consulta", () => {
    const p1 = personaEnError(1, { consulta: "RUNT_PERSONA" });
    const p2 = personaEnError(2, { consulta: "RUES", tipo_documento: "N" });
    const f = formulario("RUNT_PROPIETARIO", { personas: [p1, p2], tipo_error: "PERSONAS_CON_ERROR" });
    const runt: RuntPersonaBorrador = { tipo: "RUNT_PERSONA", inscrito: true, estado_conductor: "ACTIVO", licencias: [{ categoria: "B1", estado: "VIGENTE", vigente_hasta: "2030-01-01" }] };
    const rues: RuesBorrador = { tipo: "RUES", encontrada: true, estado_matricula: "ACTIVA", ultima_renovacion: "2026" };
    const { cuerpo, errores } = construirCuerpo(f, { [p1.persona_id]: runt, [p2.persona_id]: rues });
    expect(errores).toEqual({});
    expect(cuerpo).toEqual({
      version: 10,
      personas: [
        { persona_id: p1.persona_id, resultado: { inscrito: true, estado_conductor: "ACTIVO", licencias: [{ categoria: "B1", estado: "VIGENTE", vigente_hasta: "2030-01-01" }] } },
        { persona_id: p2.persona_id, resultado: { encontrada: true, estado_matricula: "ACTIVA", ultima_renovacion: "2026" } },
      ],
    });
  });

  it("los errores de persona llevan su índice en la ruta", () => {
    const p1 = personaEnError(1, { consulta: "RUNT_PERSONA" });
    const f = formulario("RUNT_PROPIETARIO", { personas: [p1] });
    const runt: RuntPersonaBorrador = { tipo: "RUNT_PERSONA", inscrito: true, estado_conductor: "", licencias: [{ categoria: "", estado: "", vigente_hasta: "" }] };
    const { cuerpo, errores } = construirCuerpo(f, { [p1.persona_id]: runt });
    expect(cuerpo).toBeNull();
    expect(Object.keys(errores)).toEqual(["personas[0].resultado.licencias[0].categoria"]);
  });
});
