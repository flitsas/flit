import { describe, expect, it } from "vitest";
import { ipDelCliente, parsearSaltosConfianza } from "../ip";

const h = (o: Record<string, string>) => new Headers(o);

describe("IP real del ciudadano", () => {
  it("prefiere x-real-ip aunque X-Forwarded-For traiga otra", () => {
    expect(ipDelCliente(h({ "x-real-ip": "190.1.2.3", "x-forwarded-for": "6.6.6.6, 190.1.2.3" }))).toBe("190.1.2.3");
  });

  it("sin x-real-ip usa el último salto de X-Forwarded-For, no el primero", () => {
    expect(ipDelCliente(h({ "x-forwarded-for": "1.1.1.1, 190.1.2.3" }))).toBe("190.1.2.3");
  });

  it("una IP falsificada por el cliente al inicio de la cadena se ignora", () => {
    expect(ipDelCliente(h({ "x-forwarded-for": "8.8.8.8, 9.9.9.9, 190.1.2.3" }))).toBe("190.1.2.3");
  });

  it("con 2 saltos de confianza toma el penúltimo", () => {
    expect(ipDelCliente(h({ "x-forwarded-for": "8.8.8.8, 190.1.2.3, 10.0.0.5" }), 2)).toBe("190.1.2.3");
  });

  it("si la cadena es más corta que los saltos de confianza no inventa una IP", () => {
    expect(ipDelCliente(h({ "x-forwarded-for": "190.1.2.3" }), 2)).toBeUndefined();
  });

  it("sin cabeceras devuelve undefined", () => {
    expect(ipDelCliente(h({}))).toBeUndefined();
  });

  it("descarta valores que no son una IP", () => {
    expect(ipDelCliente(h({ "x-real-ip": "<script>", "x-forwarded-for": "a b, no-ip" }))).toBeUndefined();
    expect(ipDelCliente(h({ "x-real-ip": "2800:484::1" }))).toBe("2800:484::1");
  });

  it("TRUSTED_PROXY_HOPS: por defecto 1 y valores inválidos caen a 1", () => {
    expect(parsearSaltosConfianza(undefined)).toBe(1);
    expect(parsearSaltosConfianza("2")).toBe(2);
    expect(parsearSaltosConfianza("0")).toBe(1);
    expect(parsearSaltosConfianza("abc")).toBe(1);
    expect(parsearSaltosConfianza("99")).toBe(1);
  });
});
