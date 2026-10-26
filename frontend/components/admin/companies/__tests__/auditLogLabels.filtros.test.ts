// HU #14005 — el filtro viaja con lo que el usuario VE: claves técnicas y valores booleanos.
import { describe, expect, it } from "vitest";
import {
  alternativasParaValor,
  clavesParaCampo,
  MAX_CLAVES_CAMPO,
} from "../auditLogLabels";

describe("clavesParaCampo", () => {
  it("«Inicio de sesión» incluye login (y no depende de mayúsculas ni tildes)", () => {
    expect(clavesParaCampo("Inicio de sesión")).toContain("login");
    expect(clavesParaCampo("INICIO DE SESION")).toContain("login");
  });

  it("incluye la forma técnica del propio texto para campos no catalogados", () => {
    expect(clavesParaCampo("Campo Nuevo")).toContain("campo_nuevo");
  });

  it("coincide por parte de la etiqueta y devuelve varias claves", () => {
    const claves = clavesParaCampo("improntas");
    expect(claves).toContain("generate_improntas");
    expect(clavesParaCampo("módulo")).toEqual(
      expect.arrayContaining(["tramites_module_enabled", "comparendos_module_enabled"]),
    );
  });

  it("incluye las combinaciones «Entidad: campo» de eventLabel (entidad y campo)", () => {
    const claves = clavesParaCampo("Mandatario: correo");
    expect(claves).toEqual(expect.arrayContaining(["mandate_signer", "email"]));
  });

  it("texto vacío no genera claves y se respeta el tope de la API", () => {
    expect(clavesParaCampo("   ")).toEqual([]);
    expect(clavesParaCampo("a").length).toBeLessThanOrEqual(MAX_CLAVES_CAMPO);
  });
});

describe("alternativasParaValor", () => {
  it("Habilitada/Activa/Sí → true; Deshabilitada/Inactiva → false", () => {
    expect(alternativasParaValor("Habilitada")).toEqual(["true"]);
    expect(alternativasParaValor("activa")).toEqual(["true"]);
    expect(alternativasParaValor("Sí")).toEqual(["true"]);
    expect(alternativasParaValor("Deshabilitada")).toEqual(["false"]);
    expect(alternativasParaValor("Inactiva")).toEqual(["false"]);
  });

  it("«No» solo con el texto exacto «no»", () => {
    expect(alternativasParaValor("no")).toEqual(["false"]);
    expect(alternativasParaValor("nov")).toEqual([]);
  });

  it("evita falsos positivos: 1 carácter, mitad de palabra o texto ajeno", () => {
    expect(alternativasParaValor("a")).toEqual([]);
    expect(alternativasParaValor("abilitada")).toEqual([]);
    expect(alternativasParaValor("false")).toEqual([]);
    expect(alternativasParaValor("Bogotá")).toEqual([]);
  });
});
