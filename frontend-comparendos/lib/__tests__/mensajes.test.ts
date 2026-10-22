import { describe, expect, it } from "vitest";
import { leerDestinatarios, resumenDestinatarios, sePuedeReintentar } from "@/lib/mensajes";

describe("leerDestinatarios", () => {
  it("lee el JSON del backend con nombre y copias", () => {
    const d = leerDestinatarios(JSON.stringify({ para: [{ email: "a@x.co", nombre: "Ana" }], cc: ["b@x.co"], bcc: ["c@x.co"] }));
    expect(d).toEqual({ para: ["Ana <a@x.co>"], cc: ["b@x.co"], bcc: ["c@x.co"] });
    expect(resumenDestinatarios(d)).toBe("Ana <a@x.co> y 1 más");
  });

  it("tolera texto plano y vacío", () => {
    expect(leerDestinatarios("a@x.co, b@x.co").para).toEqual(["a@x.co", "b@x.co"]);
    expect(resumenDestinatarios(leerDestinatarios(null))).toBe("—");
  });
});

describe("sePuedeReintentar", () => {
  it("solo los fallidos y nunca el SuperAdmin", () => {
    expect(sePuedeReintentar("Fallido", false)).toBe(true);
    expect(sePuedeReintentar("Rebotado", false)).toBe(false);
    expect(sePuedeReintentar("Fallido", true)).toBe(false);
  });
});
