import { describe, expect, it, vi } from "vitest";
import { obtenerMarcaPunto } from "../marca.server";

const ORIGEN = "http://gateway:4002";
const PUBLICADA = {
  origen: "organismo",
  nombre: "Tránsito de Cali",
  colores: { primario: "#0B3D91", secundario: "#3D63DD", sobrePrimario: "#FFFFFF" },
  logoUrl: "/api/v1/persona-natural/marca/imagenes/l1",
  bannerUrl: null,
  version: 2,
};
const responder = (cuerpo: unknown, status = 200) => vi.fn(async () => new Response(JSON.stringify(cuerpo), { status, headers: { "content-type": "application/json" } }));

describe("obtenerMarcaPunto (HU #14379)", () => {
  it("pide la marca al BFF SIN caché y con la IP real", async () => {
    const fetchImpl = responder(PUBLICADA);
    const marca = await obtenerMarcaPunto("CALI", { apiOrigin: ORIGEN, ipCliente: "190.1.2.3", fetchImpl });

    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${ORIGEN}/api/v1/persona-natural/punto/marca/CALI`);
    expect(init.cache).toBe("no-store");
    expect(init).not.toHaveProperty("next");
    expect((init.headers as Headers).get("x-forwarded-for")).toBe("190.1.2.3");
    expect(marca?.nombre).toBe("Tránsito de Cali");
  });

  it("codifica el código en la ruta", async () => {
    const fetchImpl = responder(PUBLICADA);
    await obtenerMarcaPunto("a/b?c", { apiOrigin: ORIGEN, fetchImpl });
    expect((fetchImpl.mock.calls[0] as unknown as [string])[0]).toBe(`${ORIGEN}/api/v1/persona-natural/punto/marca/a%2Fb%3Fc`);
  });

  it("sin marca publicada (origen flit) → null", async () => {
    expect(await obtenerMarcaPunto("X", { apiOrigin: ORIGEN, fetchImpl: responder({ origen: "flit", nombre: null, colores: null, logoUrl: null, bannerUrl: null, version: 0 }) })).toBeNull();
  });

  it.each([
    ["500", responder({}, 500)],
    ["red caída", vi.fn(async () => Promise.reject(new TypeError("fetch failed")))],
    ["JSON inválido", vi.fn(async () => new Response("<html>", { status: 200 }))],
  ])("ante una falla (%s) no rompe: marca FLIT", async (_n, fetchImpl) => {
    expect(await obtenerMarcaPunto("X", { apiOrigin: ORIGEN, fetchImpl: fetchImpl as unknown as typeof fetch })).toBeNull();
  });

  it("sin código, o con uno desmedido, ni siquiera llama", async () => {
    const fetchImpl = responder(PUBLICADA);
    expect(await obtenerMarcaPunto("", { apiOrigin: ORIGEN, fetchImpl })).toBeNull();
    expect(await obtenerMarcaPunto("x".repeat(65), { apiOrigin: ORIGEN, fetchImpl })).toBeNull();
    expect(fetchImpl).not.toHaveBeenCalled();
  });
});
