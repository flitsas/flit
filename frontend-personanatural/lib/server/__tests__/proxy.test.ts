import { describe, expect, it, vi } from "vitest";
import { reenviarAlBff, rutaSegura } from "../proxy";

const ORIGEN = "http://gateway:4002";
const post = (url: string, cuerpo?: unknown, headers: Record<string, string> = {}) =>
  new Request(url, { method: "POST", body: cuerpo === undefined ? undefined : JSON.stringify(cuerpo), headers: { "content-type": "application/json", ...headers } });

const sesionDelBff = { token: "tok-secreto-256", expiraEn: "2026-10-27T16:00:00Z", solicitudId: "s1", codigoPublico: "PN-1", estado: "borrador", tipoTramiteCodigo: "TRASPASO_STANDARD" };

describe("proxy hacia el BFF", () => {
  it("al reanudar guarda el token en cookie y lo quita de la respuesta", async () => {
    const fetchImpl = vi.fn(async () => new Response(JSON.stringify(sesionDelBff), { status: 200, headers: { "content-type": "application/json" } }));

    const resultado = await reenviarAlBff(post("https://portal/api/v1/persona-natural/sesiones/reanudar", { radicado: "PN-1", cedula: "1" }), ["sesiones", "reanudar"], { apiOrigin: ORIGEN, fetchImpl });

    expect(resultado.cookie).toEqual({ accion: "fijar", valor: "tok-secreto-256", expiraEn: new Date("2026-10-27T16:00:00Z") });
    const cuerpo = JSON.parse(String(resultado.cuerpo));
    expect(cuerpo).not.toHaveProperty("token");
    expect(JSON.stringify(cuerpo)).not.toContain("tok-secreto-256");
    expect(cuerpo).toMatchObject({ solicitudId: "s1", codigoPublico: "PN-1" });
  });

  it("también guarda la cookie al abrir un trámite nuevo (POST /sesiones)", async () => {
    const fetchImpl = vi.fn(async () => new Response(JSON.stringify(sesionDelBff), { status: 201, headers: { "content-type": "application/json" } }));

    const resultado = await reenviarAlBff(post("https://portal/api/v1/persona-natural/sesiones", {}), ["sesiones"], { apiOrigin: ORIGEN, fetchImpl });

    expect(resultado.status).toBe(201);
    expect(resultado.cookie).toMatchObject({ accion: "fijar", valor: "tok-secreto-256" });
  });

  it("adjunta el token como Bearer y la IP real, y descarta las cabeceras que no son del portal", async () => {
    const fetchImpl = vi.fn(async () => new Response("{}", { status: 200, headers: { "content-type": "application/json" } }));
    const request = new Request("https://portal/api/v1/persona-natural/solicitudes/s1?x=1", {
      headers: { cookie: "pn_sesion=abc", authorization: "Bearer del-navegador", "x-captcha-token": "cap", host: "portal", "x-otra": "z" },
    });

    await reenviarAlBff(request, ["solicitudes", "s1"], { apiOrigin: ORIGEN, tokenSesion: "tok-cookie", ipCliente: "190.1.2.3", fetchImpl });

    const [url, init] = fetchImpl.mock.calls[0] as unknown as [URL, RequestInit];
    expect(String(url)).toBe(`${ORIGEN}/api/v1/persona-natural/solicitudes/s1?x=1`);
    const cabeceras = init.headers as Headers;
    expect(cabeceras.get("authorization")).toBe("Bearer tok-cookie");
    expect(cabeceras.get("x-forwarded-for")).toBe("190.1.2.3");
    expect(cabeceras.get("x-captcha-token")).toBe("cap");
    expect(cabeceras.get("cookie")).toBeNull();
    expect(cabeceras.get("x-otra")).toBeNull();
  });

  it("no manda Authorization si no hay sesión, aunque el navegador lo intente", async () => {
    const fetchImpl = vi.fn(async () => new Response("{}", { status: 200 }));

    await reenviarAlBff(new Request("https://portal/x", { headers: { authorization: "Bearer falso" } }), ["organismos"], { apiOrigin: ORIGEN, fetchImpl });

    expect(((fetchImpl.mock.calls[0] as unknown as [URL, RequestInit])[1].headers as Headers).get("authorization")).toBeNull();
  });

  it("borra la cookie cuando el BFF dice que la sesión venció o no es válida", async () => {
    const vencida = async () => new Response(JSON.stringify({ code: "session_expired" }), { status: 401, headers: { "content-type": "application/problem+json" } });
    const sinCodigo = async () => new Response("no autorizado", { status: 401 });

    const a = await reenviarAlBff(new Request("https://portal/x"), ["solicitudes", "s1"], { apiOrigin: ORIGEN, fetchImpl: vencida });
    const b = await reenviarAlBff(new Request("https://portal/x"), ["solicitudes", "s1"], { apiOrigin: ORIGEN, fetchImpl: sinCodigo });

    expect(a.status).toBe(401);
    expect(a.cookie).toEqual({ accion: "borrar" });
    expect(b.cookie).toBeUndefined();
  });

  it("deja pasar el Retry-After de un 429", async () => {
    const limitada = async () => new Response(JSON.stringify({ code: "rate_limited" }), { status: 429, headers: { "content-type": "application/problem+json", "retry-after": "30" } });

    const resultado = await reenviarAlBff(new Request("https://portal/x"), ["organismos"], { apiOrigin: ORIGEN, fetchImpl: limitada });

    expect(resultado.status).toBe(429);
    expect(resultado.headers.get("retry-after")).toBe("30");
  });

  it("si el BFF no responde, devuelve 503 con un problem+json sin detalles", async () => {
    const resultado = await reenviarAlBff(new Request("https://portal/x"), ["organismos"], { apiOrigin: ORIGEN, fetchImpl: async () => { throw new Error("ECONNREFUSED core-personanatural:4029"); } });

    expect(resultado.status).toBe(503);
    expect(String(resultado.cuerpo)).not.toContain("ECONNREFUSED");
  });

  it("una sesión nueva sin token en la respuesta es un error 502, no una cookie vacía", async () => {
    const resultado = await reenviarAlBff(post("https://portal/x", {}), ["sesiones"], { apiOrigin: ORIGEN, fetchImpl: async () => new Response("{}", { status: 200 }) });

    expect(resultado.status).toBe(502);
    expect(resultado.cookie).toBeUndefined();
  });

  it.each([[[".."]], [["a", ".."]], [["a%2F..%2Fb"]], [[]], [["a", ""]], [["x?y"]]])("rechaza rutas que se salen del prefijo: %j", async (path) => {
    expect(rutaSegura(path)).toBe(false);
    const fetchImpl = vi.fn();

    const resultado = await reenviarAlBff(new Request("https://portal/x"), path, { apiOrigin: ORIGEN, fetchImpl: fetchImpl as unknown as typeof fetch });

    expect(resultado.status).toBe(400);
    expect(fetchImpl).not.toHaveBeenCalled();
  });
});
