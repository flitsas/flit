// @vitest-environment node
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { APP, CLAVE_INTERNA, GATEWAY, ahora, cabeceraCookie, entornoDeServidor, jwt } from "./sesion-de-prueba";
import { authRoutes } from "@/lib/auth.server";
import { GET } from "@/app/api/v1/[...path]/route";

// Uso de ejemplo: GET(request, { params: Promise.resolve({ path: ["diagnostico", "sesion"] }) }) → gateway con Bearer

beforeEach(entornoDeServidor);
afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

const contexto = (path: string[]) => ({ params: Promise.resolve({ path }) });

/** Abre una sesión real de Diagnóstico (login + callback contra un hub simulado) y devuelve su cabecera Cookie. */
async function sesionDiagnostico(accessToken: string): Promise<string> {
  const login = await authRoutes.login(new Request(`${APP}/auth/login`));
  const state = new URL(login.headers.get("location")!).searchParams.get("state")!;
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json({ access_token: accessToken, refresh_token: "r-1", expires_in: 900 })));
  const callback = await authRoutes.callback(
    new Request(`${APP}/auth/callback?code=c-1&state=${state}`, { headers: { cookie: cabeceraCookie(login.headers.getSetCookie()) } }),
  );
  return cabeceraCookie(callback.headers.getSetCookie());
}

describe("BFF /api/v1", () => {
  it("reenvía con Bearer de audiencia diagnostico", async () => {
    const accessToken = jwt({ sub: "u-1", aud: "diagnostico", exp: ahora() + 900 });
    const cookie = await sesionDiagnostico(accessToken);
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ empresa: { id: "e-1" } }));
    vi.stubGlobal("fetch", fetchMock);

    const respuesta = await GET(
      new Request(`${APP}/api/v1/diagnostico/sesion`, { headers: { host: "dev.diagnostico.flitsas.online", cookie, authorization: "Bearer ajeno" } }),
      contexto(["diagnostico", "sesion"]),
    );

    expect(respuesta.status).toBe(200);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${GATEWAY}/api/v1/diagnostico/sesion`);
    const headers = init.headers as Headers;
    expect(headers.get("authorization")).toBe(`Bearer ${accessToken}`);
    const claims = JSON.parse(Buffer.from(accessToken.split(".")[1], "base64url").toString()) as { aud: string };
    expect(claims.aud).toBe("diagnostico");
    expect(headers.get("x-internal-key")).toBe(CLAVE_INTERNA);
    expect(headers.get("x-flit-domain")).toBe("dev.diagnostico.flitsas.online");
    expect(headers.has("cookie")).toBe(false);
  });

  it("sin sesión responde 401 SESSION_EXPIRED y no llama a la API", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const respuesta = await GET(new Request(`${APP}/api/v1/diagnostico/sesion`), contexto(["diagnostico", "sesion"]));

    expect(respuesta.status).toBe(401);
    expect(await respuesta.json()).toMatchObject({ code: "SESSION_EXPIRED" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("devuelve tal cual el 403 PRODUCTO_NO_HABILITADO del guard de suscripción", async () => {
    const cookie = await sesionDiagnostico(jwt({ sub: "u-1", aud: "diagnostico", exp: ahora() + 900 }));
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json({ code: "PRODUCTO_NO_HABILITADO", error: "Sin Diagnóstico" }, { status: 403 })));

    const respuesta = await GET(new Request(`${APP}/api/v1/diagnostico/sesion`, { headers: { cookie } }), contexto(["diagnostico", "sesion"]));

    expect(respuesta.status).toBe(403);
    expect(await respuesta.json()).toMatchObject({ code: "PRODUCTO_NO_HABILITADO" });
  });
});
