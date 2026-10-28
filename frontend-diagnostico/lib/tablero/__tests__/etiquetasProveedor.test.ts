import { describe, expect, it } from "vitest";
import { CLAVES_OCULTAS, ETIQUETAS_PROVEEDOR, etiquetaCampo, etiquetaCampoProveedor, textoValor, textoValorProveedor } from "@/lib/tablero/etiquetasRespuesta";

// Uso de ejemplo: etiquetaCampoProveedor("legalRepresentatives") → «Representantes legales»; etiquetaCampoProveedor("x_raro")
// → «x_raro» (HU #14154: en la respuesta íntegra del proveedor las claves desconocidas salen con su nombre original).
// textoValorProveedor(null) → «—»; textoValorProveedor([]) → «Sin registros»; los textos del proveedor, tal cual.

describe("HU #14154 AC3 — etiquetaCampoProveedor", () => {
  it("claves conocidas del RUNT persona y del RUES en español", () => {
    expect(etiquetaCampoProveedor("legalRepresentatives")).toBe("Representantes legales");
    expect(etiquetaCampoProveedor("economicActivities")).toBe("Actividades económicas");
    expect(etiquetaCampoProveedor("estadoLicencia")).toBe("Estado de la licencia");
    expect(etiquetaCampoProveedor("signature")).toBe("Firma");
  });

  it("reutiliza el diccionario de la respuesta de la fuente (snake_case del .proto)", () => {
    expect(etiquetaCampoProveedor("estado_conductor")).toBe("Estado del conductor");
    expect(etiquetaCampoProveedor("razon_social")).toBe("Razón social");
  });

  it("desconocidas: el nombre original, sin humanizar; vacía no lanza", () => {
    expect(etiquetaCampoProveedor("campo_sin_traducir")).toBe("campo_sin_traducir");
    expect(etiquetaCampoProveedor("id_consulta")).toBe("id_consulta");
    expect(() => etiquetaCampoProveedor("")).not.toThrow();
  });

  it("contrato: ningún nombre del diccionario del proveedor queda vacío", () => {
    for (const [clave, texto] of Object.entries(ETIQUETAS_PROVEEDOR)) expect(texto.trim(), clave).not.toBe("");
  });
});

describe("HU #14154 AC2 — textoValorProveedor", () => {
  it("null, undefined, \"\" y objeto vacío → «—»; lista vacía → «Sin registros»", () => {
    expect(textoValorProveedor(null)).toBe("—");
    expect(textoValorProveedor(undefined)).toBe("—");
    expect(textoValorProveedor("")).toBe("—");
    expect(textoValorProveedor({})).toBe("—");
    expect(textoValorProveedor([])).toBe("Sin registros");
  });

  it("booleanos Sí/No, números y textos tal cual (sin traducir enums)", () => {
    expect(textoValorProveedor(true)).toBe("Sí");
    expect(textoValorProveedor(false)).toBe("No");
    expect(textoValorProveedor(0)).toBe("0");
    expect(textoValorProveedor("VIGENCIA_VIGENTE")).toBe("VIGENCIA_VIGENTE");
  });

  it("AC6: las claves ocultas de la respuesta de la fuente siguen definidas (no se tocan)", () => {
    expect(CLAVES_OCULTAS.has("http_status")).toBe(true);
    expect(CLAVES_OCULTAS.has("id")).toBe(true);
  });
});

describe("HU #14154 H4 — claves que coinciden con Object.prototype no heredan sus propiedades", () => {
  const CLAVES = ["constructor", "toString", "__proto__", "hasOwnProperty", "valueOf"];

  it("etiquetaCampoProveedor devuelve el nombre original de la clave", () => {
    for (const clave of CLAVES) expect(etiquetaCampoProveedor(clave)).toBe(clave);
  });

  it("etiquetaCampo las humaniza (no devuelve funciones ni objetos del prototipo)", () => {
    expect(etiquetaCampo("constructor")).toBe("Constructor");
    expect(etiquetaCampo("toString")).toBe("To string");
    expect(etiquetaCampo("hasOwnProperty")).toBe("Has own property");
    for (const clave of CLAVES) expect(typeof etiquetaCampo(clave)).toBe("string");
  });

  it("textoValor no traduce textos que coinciden con Object.prototype", () => {
    for (const clave of CLAVES) expect(textoValor(clave)).toBe(clave);
  });
});
