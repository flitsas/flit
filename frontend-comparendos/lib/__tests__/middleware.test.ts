import { describe, expect, it, vi } from "vitest";

vi.mock("@flit/auth/request", () => ({ claimsTokenFromCookieHeader: vi.fn() }));

import { config } from "@/middleware";

// El matcher de Next.js es una expresión de ruta; se evalúa como regex anclada para comprobar qué exige sesión.
const exigeSesion = (ruta: string) => new RegExp(`^${config.matcher[0]}$`).test(ruta);

describe("middleware de sesión", () => {
  it("exige sesión en las pantallas", () => {
    expect(exigeSesion("/")).toBe(true);
    expect(exigeSesion("/admin/ingesta")).toBe(true);
  });

  it("deja pasar sin sesión el píxel, el portal, la API y el login", () => {
    expect(exigeSesion("/publico/t/abc.gif")).toBe(false);
    expect(exigeSesion("/portal/token")).toBe(false);
    expect(exigeSesion("/api/v1/comparendos/sesion")).toBe(false);
    expect(exigeSesion("/auth/login")).toBe(false);
  });
});
