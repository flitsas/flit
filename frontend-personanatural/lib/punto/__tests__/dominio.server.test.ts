import { describe, expect, it, vi } from "vitest";
import { hostValido, resolverDominioPortal } from "../dominio.server";

const ORIGEN = "http://gateway:4002";
const responder = (cuerpo: unknown, status = 200) =>
  vi.fn(
    async () =>
      new Response(JSON.stringify(cuerpo), {
        status,
        headers: { "content-type": "application/json" },
      }),
  );

describe("resolverDominioPortal (HU #14386)", () => {
  it("pide el host al BFF SIN caché y con la IP real", async () => {
    const fetchImpl = responder({ codigo: "CALI" });
    const codigo = await resolverDominioPortal("Tramites.Cali.gov.co", {
      apiOrigin: ORIGEN,
      ipCliente: "190.1.2.3",
      fetchImpl,
    });

    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${ORIGEN}/api/v1/persona-natural/punto/dominio/tramites.cali.gov.co`);
    expect(init.cache).toBe("no-store");
    expect(init).not.toHaveProperty("next");
    expect((init.headers as Headers).get("x-forwarded-for")).toBe("190.1.2.3");
    expect(codigo).toBe("CALI");
  });

  it.each([
    ["localhost"],
    ["localhost:4031"],
    ["10.0.0.5"],
    ["a_b.com"],
    ["a/b.com"],
    ["-x.com"],
    [""],
    [null],
    [undefined],
    ["x".repeat(254)],
  ])("un host inválido (%s) no llega al BFF", async (host) => {
    const fetchImpl = responder({ codigo: "CALI" });
    expect(await resolverDominioPortal(host, { apiOrigin: ORIGEN, fetchImpl })).toBeNull();
    expect(fetchImpl).not.toHaveBeenCalled();
  });

  it.each([
    ["404", responder({}, 404)],
    ["500", responder({}, 500)],
    ["sin código", responder({})],
    ["código vacío", responder({ codigo: "  " })],
    ["red caída", vi.fn(async () => Promise.reject(new TypeError("fetch failed")))],
  ])("%s → null (la web queda como hoy)", async (_n, fetchImpl) => {
    expect(
      await resolverDominioPortal("tramites.cali.gov.co", {
        apiOrigin: ORIGEN,
        fetchImpl: fetchImpl as unknown as typeof fetch,
      }),
    ).toBeNull();
  });

  it("hostValido normaliza mayúsculas y punto final", () => {
    expect(hostValido("Tramites.Cali.GOV.co.")).toBe("tramites.cali.gov.co");
  });
});
