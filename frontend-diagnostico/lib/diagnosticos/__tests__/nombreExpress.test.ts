import { describe, expect, it } from "vitest";
import { nombreExpress } from "@/lib/diagnosticos/nombreExpress";
import { NOMBRE_MAXIMO } from "@/lib/diagnosticos/api";

// Uso de ejemplo: nombreExpress("ABC123") → "Express ABC123" (HU #14087: sin fecha; el servidor arma el mismo nombre)

describe("nombreExpress (F01-C10, HU #14087 AC2)", () => {
  it("arma «Express IDENTIFICADOR», sin fecha", () => {
    expect(nombreExpress("ABC123")).toBe("Express ABC123");
  });

  it("normaliza el identificador: mayúsculas y sin espacios", () => {
    expect(nombreExpress(" abc 123 ")).toBe("Express ABC123");
  });

  it("no lleva ningún día aunque cambie el reloj", () => {
    expect(nombreExpress("ABC123")).not.toMatch(/\d{2}\/\d{2}\/\d{4}/);
  });

  it("con un VIN de 17 caracteres no pasa del máximo del nombre", () => {
    const nombre = nombreExpress("1HGBH41JXMN109186");
    expect(nombre).toBe("Express 1HGBH41JXMN109186");
    expect(nombre.length).toBeLessThanOrEqual(NOMBRE_MAXIMO);
  });

  it("usa la normalización de fieldRules (como el servidor): sin guiones ni símbolos", () => {
    expect(nombreExpress("abc-123")).toBe("Express ABC123");
  });
});
