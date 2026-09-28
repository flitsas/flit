import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("server-only", () => ({}));

import { proxyToApi } from "../api-proxy.server";

const config = { apiOrigin: "http://gateway:4002", internalApiKey: "clave", loginUrl: "/login" };

afterEach(() => vi.unstubAllGlobals());

function stubFetch(response: Response = new Response("{}", { status: 200 })) {
  const fetchMock = vi.fn().mockResolvedValue(response);
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

describe("proxyToApi", () => {
  it("reenvía al gateway con la ruta, la consulta y el token del usuario", async () => {
    const fetchMock = stubFetch();
    const request = new Request("https://dev.flitsas.online/api/v1/platform/me/apps?x=1", {
      headers: { host: "dev.flitsas.online", authorization: "Bearer t" },
    });

    await proxyToApi(request, "/api/v1", ["platform", "me", "apps"], config);

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://gateway:4002/api/v1/platform/me/apps?x=1");
    const headers = init.headers as Headers;
    expect(headers.get("authorization")).toBe("Bearer t");
    expect(init.body).toBeUndefined();
  });

  it("sella el dominio con el host real y descarta el sello y la clave que mande el navegador", async () => {
    const fetchMock = stubFetch();
    const request = new Request("https://marca.example.com/api/v1/public/branding", {
      headers: { host: "Marca.Example.com:443", "x-flit-domain": "otra-red.com", "x-internal-key": "robada" },
    });

    await proxyToApi(request, "/api/v1", ["public", "branding"], config);

    const headers = (fetchMock.mock.calls[0][1] as RequestInit).headers as Headers;
    expect(headers.get("x-flit-domain")).toBe("marca.example.com");
    expect(headers.get("x-internal-key")).toBe("clave");
  });

  it("sin clave interna configurada no envía la cabecera", async () => {
    const fetchMock = stubFetch();
    const request = new Request("https://dev.flitsas.online/api/v1/a", { headers: { host: "dev.flitsas.online", "x-internal-key": "robada" } });

    await proxyToApi(request, "/api/v1", ["a"], { ...config, internalApiKey: undefined });

    expect(((fetchMock.mock.calls[0][1] as RequestInit).headers as Headers).has("x-internal-key")).toBe(false);
  });

  it("reenvía el cuerpo en las escrituras y devuelve estado y cabeceras de la API", async () => {
    const fetchMock = stubFetch(new Response('{"code":"X"}', { status: 409, headers: { "content-type": "application/json", "content-encoding": "gzip" } }));
    const request = new Request("https://dev.flitsas.online/api/v1/a/b", {
      method: "PUT",
      headers: { host: "dev.flitsas.online", "content-type": "application/json" },
      body: '{"x":1}',
    });

    const response = await proxyToApi(request, "/api/v1", ["a", "b"], config);

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(init.method).toBe("PUT");
    expect(new TextDecoder().decode(init.body as ArrayBuffer)).toBe('{"x":1}');
    expect(response.status).toBe(409);
    expect(response.headers.get("content-type")).toBe("application/json");
    expect(response.headers.has("content-encoding")).toBe(false);
    expect(await response.json()).toEqual({ code: "X" });
  });

  it("si el gateway no responde devuelve 502 API_UNAVAILABLE", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("fetch failed")));
    const request = new Request("https://dev.flitsas.online/api/v1/a", { headers: { host: "dev.flitsas.online" } });

    const response = await proxyToApi(request, "/api/v1", ["a"], config);

    expect(response.status).toBe(502);
    expect(await response.json()).toMatchObject({ code: "API_UNAVAILABLE" });
  });

  it("reenvía el servidor OIDC con su propio prefijo y deja pasar la cookie y la redirección", async () => {
    const upstream = new Response(null, { status: 302, headers: { location: "https://dev.flitsas.online/login?returnUrl=%2Fx" } });
    upstream.headers.append("set-cookie", "flit_hub=a; path=/; secure; httponly; samesite=lax");
    const fetchMock = stubFetch(upstream);
    const request = new Request("https://dev.flitsas.online/connect/authorize?client_id=tramites", { headers: { host: "dev.flitsas.online" } });

    const response = await proxyToApi(request, "/connect", ["authorize"], config);

    expect(fetchMock.mock.calls[0][0]).toBe("http://gateway:4002/connect/authorize?client_id=tramites");
    expect(response.status).toBe(302);
    expect(response.headers.get("location")).toBe("https://dev.flitsas.online/login?returnUrl=%2Fx");
    expect(response.headers.get("set-cookie")).toContain("flit_hub=a");
  });

  it("codifica cada segmento de la ruta", async () => {
    const fetchMock = stubFetch();
    const request = new Request("https://dev.flitsas.online/api/v1/x", { headers: { host: "dev.flitsas.online" } });

    await proxyToApi(request, "/api/v1", ["docs", "a b", "..%2f"], config);

    expect(fetchMock.mock.calls[0][0]).toBe("http://gateway:4002/api/v1/docs/a%20b/..%252f");
  });
});
