import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";
import { forgetNetworkHubs } from "@flit/auth/network";
import { hubNav } from "@/components/HubShell";
import { middleware } from "@/middleware";
import { resolveTramitesUrl } from "../tramites-url";

// En el hub de una red de Marca Blanca, la administración (que vive en Trámites) se abre en el Trámites de la red. En el
// de FLIT su usuario recibe NETWORK_DOMAIN_REQUIRED y, tras volver a entrar, acaba en el inicio sin llegar a la pantalla.
const FLIT = "https://tramites.flitsas.com";
const RED = "https://tramites.cliente.com";

function stubNetwork(body: unknown, ok = true) {
  const fetchMock = vi.fn((_url: string, _init?: RequestInit) => Promise.resolve(Response.json(body, { status: ok ? 200 : 500 })));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

beforeEach(() => {
  forgetNetworkHubs();
  vi.stubEnv("FLIT_NETWORK_DOMAINS_ENABLED", "true");
  vi.stubEnv("CORE_API_ORIGIN", "http://gateway:6002");
  vi.stubEnv("FLIT_INTERNAL_API_KEY", "clave");
});

afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

describe("resolveTramitesUrl", () => {
  it("en el dominio de una red da su Trámites y sella el host al preguntar", async () => {
    const fetchMock = stubNetwork({ hubUrl: "https://consola.cliente.com", tramitesUrl: RED });

    await expect(resolveTramitesUrl("Consola.Cliente.com:443", FLIT)).resolves.toBe(RED);

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://gateway:6002/api/v1/public/network");
    expect(init?.headers).toMatchObject({ "x-flit-domain": "consola.cliente.com", "x-internal-key": "clave" });
  });

  it("en FLIT o en una red sin dominio de Trámites se queda con TRAMITES_URL", async () => {
    stubNetwork({ hubUrl: null, tramitesUrl: null });
    await expect(resolveTramitesUrl("app.flitsas.com", FLIT)).resolves.toBe(FLIT);
  });

  it("si la API no responde, o aún no devuelve el campo, se queda con TRAMITES_URL", async () => {
    stubNetwork({}, false);
    await expect(resolveTramitesUrl("consola.cliente.com", FLIT)).resolves.toBe(FLIT);

    forgetNetworkHubs();
    stubNetwork({ hubUrl: "https://consola.cliente.com" });
    await expect(resolveTramitesUrl("consola.cliente.com", FLIT)).resolves.toBe(FLIT);
  });

  it("con la bandera apagada no pregunta", async () => {
    vi.stubEnv("FLIT_NETWORK_DOMAINS_ENABLED", "false");
    const fetchMock = stubNetwork({ tramitesUrl: RED });

    await expect(resolveTramitesUrl("consola.cliente.com", FLIT)).resolves.toBe(FLIT);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("menú del hub", () => {
  it("«Usuarios y roles» y «Mi empresa» usan el Trámites que se le pasa", () => {
    const hrefs = Object.fromEntries(hubNav(RED).items.map((item) => [item.key, item.href]));
    expect(hrefs.usuarios).toBe(`${RED}/?m=usuarios`);
    expect(hrefs.empresa).toBe(`${RED}/admin/companies`);
  });
});

describe("middleware del hub", () => {
  const request = (path: string, host: string) =>
    new NextRequest(new URL(path, `https://${host}`), { headers: { host } });

  beforeEach(() => {
    vi.stubEnv("FLIT_TRAMITES_HOST_ENABLED", "true");
    vi.stubEnv("TRAMITES_URL", FLIT);
  });

  it("en el dominio de una red, las rutas de Trámites van al Trámites de la red", async () => {
    stubNetwork({ hubUrl: "https://consola.cliente.com", tramitesUrl: RED });

    const response = await middleware(request("/?m=usuarios", "consola.cliente.com"));

    expect(response.status).toBe(308);
    expect(response.headers.get("location")).toBe(`${RED}/?m=usuarios`);
  });

  it("en el dominio de FLIT siguen yendo a TRAMITES_URL", async () => {
    stubNetwork({ hubUrl: null, tramitesUrl: null });

    const response = await middleware(request("/admin/companies", "app.flitsas.com"));

    expect(response.headers.get("location")).toBe(`${FLIT}/admin/companies`);
  });

  it("lo que atiende el hub no consulta la red", async () => {
    const fetchMock = stubNetwork({ tramitesUrl: RED });

    const response = await middleware(request("/login", "consola.cliente.com"));

    expect(response.headers.get("location")).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
