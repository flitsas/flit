import { describe, expect, it } from "vitest";
import { nombreExpress } from "@/lib/diagnosticos/nombreExpress";
import { NOMBRE_MAXIMO } from "@/lib/diagnosticos/api";

// Uso de ejemplo: nombreExpress("ABC123", new Date("2026-10-21T15:30:00Z")) → "Express ABC123 21/10/2026"

describe("nombreExpress (F01-C10)", () => {
  it("arma el nombre con el identificador y la fecha del día en Colombia", () => {
    expect(nombreExpress("ABC123", new Date("2026-10-21T15:30:00Z"))).toBe("Express ABC123 21/10/2026");
  });

  it("normaliza el identificador: mayúsculas y sin espacios", () => {
    expect(nombreExpress(" abc 123 ", new Date("2026-10-21T15:30:00Z"))).toBe("Express ABC123 21/10/2026");
  });

  it("usa el día de Bogotá aunque en UTC ya sea el día siguiente", () => {
    expect(nombreExpress("ABC123", new Date("2026-10-22T03:00:00Z"))).toBe("Express ABC123 21/10/2026");
  });

  it("con un VIN de 17 caracteres no pasa del máximo del nombre", () => {
    const nombre = nombreExpress("1HGBH41JXMN109186", new Date("2026-01-05T12:00:00Z"));
    expect(nombre).toBe("Express 1HGBH41JXMN109186 05/01/2026");
    expect(nombre.length).toBeLessThanOrEqual(NOMBRE_MAXIMO);
  });

  it("usa la normalización de fieldRules (como el servidor): sin guiones ni símbolos", () => {
    expect(nombreExpress("abc-123", new Date("2026-10-21T15:30:00Z"))).toBe("Express ABC123 21/10/2026");
  });
});
