// @vitest-environment node
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { APP, CLAVE_INTERNA, GATEWAY, ahora, cabeceraCookie, entornoDeServidor, jwt } from "./sesion-de-prueba";
import { authRoutes } from "@/lib/auth.server";
import { GET, POST, PUT } from "@/app/api/v1/[...path]/route";

// Uso de ejemplo: GET(request, { params: Promise.resolve({ path: ["impuestos", "sesion"] }) }) → gateway con Bearer
// El proxy es genérico: no conoce las rutas del contrato impuestos.v1 (lo escribe otro carril); reenvía cualquier
// /api/v1/* con la sesión de Impuestos.

beforeEach(entornoDeServidor);
afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

const contexto = (path: string[]) => ({ params: Promise.resolve({ path }) });

/** Abre una sesión real de Impuestos (login + callback contra un hub simulado) y devuelve su cabecera Cookie. */
async function sesionImpuestos(accessToken: string): Promise<string> {
  const login = await authRoutes.login(new Request(`${APP}/auth/login`));
  const state = new URL(login.headers.get("location")!).searchParams.get("state")!;
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json({ access_token: accessToken, refresh_token: "r-1", expires_in: 900 })));
  const callback = await authRoutes.callback(
    new Request(`${APP}/auth/callback?code=c-1&state=${state}`, { headers: { cookie: cabeceraCookie(login.headers.getSetCookie()) } }),
  );
  return cabeceraCookie(callback.headers.getSetCookie());
}

describe("BFF /api/v1 de Impuestos", () => {
  it("reenvía al gateway con el Bearer de audiencia impuestos, la clave interna y el dominio; nunca la cookie", async () => {
    const accessToken = jwt({ sub: "u-1", aud: "impuestos", exp: ahora() + 900 });
    const cookie = await sesionImpuestos(accessToken);
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ empresa: { id: "e-1" } }));
    vi.stubGlobal("fetch", fetchMock);

    const respuesta = await GET(
      new Request(`${APP}/api/v1/impuestos/sesion`, { headers: { host: "dev.impuestos.flitsas.online", cookie, authorization: "Bearer ajeno" } }),
      contexto(["impuestos", "sesion"]),
    );

    expect(respuesta.status).toBe(200);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${GATEWAY}/api/v1/impuestos/sesion`);
    const headers = init.headers as Headers;
    expect(headers.get("authorization")).toBe(`Bearer ${accessToken}`);
    const claims = JSON.parse(Buffer.from(accessToken.split(".")[1], "base64url").toString()) as { aud: string };
    expect(claims.aud).toBe("impuestos");
    expect(headers.get("x-internal-key")).toBe(CLAVE_INTERNA);
    expect(headers.get("x-flit-domain")).toBe("dev.impuestos.flitsas.online");
    expect(headers.has("cookie")).toBe(false);
  });

  it("es genérico: cualquier ruta anidada conserva la query (?empresa= del Super Admin) sin conocer el contrato", async () => {
    const cookie = await sesionImpuestos(jwt({ sub: "u-1", aud: "impuestos", exp: ahora() + 900 }));
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ items: [] }));
    vi.stubGlobal("fetch", fetchMock);

    const ruta = ["impuestos", "una", "ruta", "cualquiera", "x-1"];
    const respuesta = await GET(new Request(`${APP}/api/v1/${ruta.join("/")}?empresa=e-9&page=2`, { headers: { cookie } }), contexto(ruta));

    expect(respuesta.status).toBe(200);
    expect(fetchMock.mock.calls[0][0]).toBe(`${GATEWAY}/api/v1/impuestos/una/ruta/cualquiera/x-1?empresa=e-9&page=2`);
  });

  it("reenvía el método y el cuerpo de POST y PUT", async () => {
    const cookie = await sesionImpuestos(jwt({ sub: "u-1", aud: "impuestos", exp: ahora() + 900 }));
    const fetchMock = vi.fn().mockImplementation(async () => Response.json({ ok: true }, { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);

    const cuerpo = JSON.stringify({ valor: "ABC123" });
    const enviada = await POST(
      new Request(`${APP}/api/v1/impuestos/recurso`, { method: "POST", body: cuerpo, headers: { cookie, "content-type": "application/json" } }),
      contexto(["impuestos", "recurso"]),
    );
    expect(enviada.status).toBe(201);
    const [, initPost] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(initPost.method).toBe("POST");
    expect(await new Response(initPost.body as BodyInit).text()).toBe(cuerpo);

    await PUT(new Request(`${APP}/api/v1/impuestos/recurso`, { method: "PUT", body: cuerpo, headers: { cookie } }), contexto(["impuestos", "recurso"]));
    expect((fetchMock.mock.calls[1] as [string, RequestInit])[1].method).toBe("PUT");
  });

  it("sin sesión responde 401 SESSION_EXPIRED y no llama a la API", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const respuesta = await GET(new Request(`${APP}/api/v1/impuestos/sesion`), contexto(["impuestos", "sesion"]));

    expect(respuesta.status).toBe(401);
    expect(await respuesta.json()).toMatchObject({ code: "SESSION_EXPIRED" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("la cookie de otro producto (diagnostico) no sirve para el proxy de Impuestos", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const respuesta = await GET(
      new Request(`${APP}/api/v1/impuestos/sesion`, { headers: { cookie: "flit_session_diagnostico=sellada" } }),
      contexto(["impuestos", "sesion"]),
    );

    expect(respuesta.status).toBe(401);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("devuelve tal cual el 403 del guard de suscripción", async () => {
    const cookie = await sesionImpuestos(jwt({ sub: "u-1", aud: "impuestos", exp: ahora() + 900 }));
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json({ code: "PRODUCTO_NO_HABILITADO", error: "Sin Impuestos" }, { status: 403 })));

    const respuesta = await GET(new Request(`${APP}/api/v1/impuestos/sesion`, { headers: { cookie } }), contexto(["impuestos", "sesion"]));

    expect(respuesta.status).toBe(403);
    expect(await respuesta.json()).toMatchObject({ code: "PRODUCTO_NO_HABILITADO" });
  });
});
