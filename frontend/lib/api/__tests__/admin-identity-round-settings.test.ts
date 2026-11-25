import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  MAX_CORREOS,
  getIdentityRoundSettings,
  interpretarErrorRondas,
  putIdentityRoundSettings,
  validarNuevoCorreo,
  validarRondas,
  type IdentityRoundSettings,
} from "../admin-identity-round-settings";
import { ApiError } from "../types";

const RUTA = "/api/v1/admin/identity/round-settings";
const AJUSTES: IdentityRoundSettings = {
  rondasMaximas: 3,
  disparadorAlerta: "al_maximo",
  correosAlerta: ["ops@flit.co"],
};

function respuesta(status: number, cuerpo?: unknown): Response {
  return new Response(cuerpo === undefined ? null : JSON.stringify(cuerpo), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("admin-identity-round-settings — cliente", () => {
  const fetchMock = vi.fn();
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
  });
  afterEach(() => vi.unstubAllGlobals());

  it("GET a la ruta del contrato", async () => {
    fetchMock.mockResolvedValue(respuesta(200, AJUSTES));
    const r = await getIdentityRoundSettings();
    expect(new URL(String(fetchMock.mock.calls[0][0])).pathname).toBe(RUTA);
    expect(fetchMock.mock.calls[0][1].method).toBe("GET");
    expect(r).toEqual(AJUSTES);
  });

  it("PUT envía solo los tres campos del contrato y devuelve el objeto guardado", async () => {
    const nuevo = { ...AJUSTES, rondasMaximas: 5 };
    fetchMock.mockResolvedValue(respuesta(200, nuevo));
    const r = await putIdentityRoundSettings(nuevo);
    expect(fetchMock.mock.calls[0][1].method).toBe("PUT");
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual(nuevo);
    expect(r).toEqual(nuevo);
  });

  it("403 se propaga como ApiError y se interpreta como sin_permiso", async () => {
    fetchMock.mockResolvedValue(respuesta(403, { error: "forbidden" }));
    const err = await putIdentityRoundSettings(AJUSTES).catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(interpretarErrorRondas(err).tipo).toBe("sin_permiso");
  });
});

describe("validaciones", () => {
  it("rango de rondas 1..10 y entero", () => {
    expect(validarRondas("1")).toBeNull();
    expect(validarRondas("10")).toBeNull();
    expect(validarRondas("0")).toMatch(/entre 1 y 10/i);
    expect(validarRondas("11")).toMatch(/entre 1 y 10/i);
    expect(validarRondas("")).not.toBeNull();
    expect(validarRondas("2.5")).not.toBeNull();
  });

  it("correo: formato, duplicado sin distinguir mayúsculas y máximo", () => {
    expect(validarNuevoCorreo("ops@flit.co", [])).toBeNull();
    expect(validarNuevoCorreo("no-es-correo", [])).toMatch(/formato/i);
    expect(validarNuevoCorreo("OPS@flit.co", ["ops@flit.co"])).toMatch(/ya está/i);
    const veinte = Array.from({ length: MAX_CORREOS }, (_, i) => `u${i}@flit.co`);
    expect(validarNuevoCorreo("otro@flit.co", veinte)).toMatch(/máximo/i);
  });
});

describe("interpretarErrorRondas — 400 con details[]", () => {
  it("asigna cada detalle a su campo (texto u objeto) y deja el resto como general", () => {
    const r = interpretarErrorRondas(
      new ApiError(400, "x", {
        details: [
          "rondasMaximas debe estar entre 1 y 10",
          { field: "correosAlerta[1]", message: "Correo duplicado" },
          "algo inesperado",
        ],
      }),
    );
    expect(r.tipo).toBe("invalido");
    if (r.tipo !== "invalido") return;
    expect(r.errores.rondasMaximas).toMatch(/entre 1 y 10/);
    expect(r.errores.correosAlerta).toMatch(/duplicado/i);
    expect(r.general).toBe("algo inesperado");
  });

  it("error de red u otro status → otro", () => {
    expect(interpretarErrorRondas(new ApiError(500, "x")).tipo).toBe("otro");
    expect(interpretarErrorRondas(new Error("boom")).tipo).toBe("otro");
  });
});
