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

describe("proxy hacia el BFF: archivos (multipart)", () => {
  it("reenvía un multipart de 20 MB intacto, con su boundary, sin recortar ni convertir el cuerpo", async () => {
    const frontera = "----pn-frontera";
    const archivo = new Uint8Array(20 * 1024 * 1024).fill(7);
    const cabeza = new TextEncoder().encode(`--${frontera}\r\nContent-Disposition: form-data; name="tipo"\r\n\r\npaz_salvo\r\n--${frontera}\r\nContent-Disposition: form-data; name="archivo"; filename="a.pdf"\r\nContent-Type: application/pdf\r\n\r\n`);
    const cola = new TextEncoder().encode(`\r\n--${frontera}--\r\n`);
    const cuerpo = new Uint8Array(cabeza.length + archivo.length + cola.length);
    cuerpo.set(cabeza, 0);
    cuerpo.set(archivo, cabeza.length);
    cuerpo.set(cola, cabeza.length + archivo.length);
    const fetchImpl = vi.fn(async () => new Response(JSON.stringify({ id: "a1" }), { status: 201, headers: { "content-type": "application/json" } }));
    const request = new Request("https://portal/api/v1/persona-natural/solicitudes/s1/adjuntos", {
      method: "POST",
      body: cuerpo,
      headers: { "content-type": `multipart/form-data; boundary=${frontera}` },
    });

    const resultado = await reenviarAlBff(request, ["solicitudes", "s1", "adjuntos"], { apiOrigin: ORIGEN, tokenSesion: "tok", fetchImpl });

    const [, init] = fetchImpl.mock.calls[0] as unknown as [URL, RequestInit];
    expect((init.headers as Headers).get("content-type")).toBe(`multipart/form-data; boundary=${frontera}`);
    expect((init.headers as Headers).get("authorization")).toBe("Bearer tok");
    const enviado = new Uint8Array(init.body as ArrayBuffer);
    expect(enviado.length).toBe(cuerpo.length);
    expect(Array.from(enviado.subarray(0, cabeza.length))).toEqual(Array.from(cabeza));
    expect(enviado[cabeza.length + 1000]).toBe(7);
    expect(resultado.status).toBe(201);
  });
});

describe("proxy: configuración pública del portal (HU #14073)", () => {
  it("la reenvía anónima: sin token de sesión, con If-None-Match, y devuelve ETag y Cache-Control", async () => {
    const fetchImpl = vi.fn(async () => new Response(new Uint8Array([1, 2, 3]), { status: 200, headers: { "content-type": "image/png", etag: '"abc"', "cache-control": "public, max-age=60" } }));
    const request = new Request("https://portal/api/v1/persona-natural/configuracion/banners/b1/imagen?v=abc", { headers: { "if-none-match": '"abc"', cookie: "pn_sesion=t" } });

    const r = await reenviarAlBff(request, ["configuracion", "banners", "b1", "imagen"], { apiOrigin: ORIGEN, tokenSesion: "tok-cookie", fetchImpl });

    const [url, init] = fetchImpl.mock.calls[0] as unknown as [URL, RequestInit];
    expect(String(url)).toBe(`${ORIGEN}/api/v1/persona-natural/configuracion/banners/b1/imagen?v=abc`);
    expect((init.headers as Headers).get("authorization")).toBeNull();
    expect((init.headers as Headers).get("if-none-match")).toBe('"abc"');
    expect(r.headers.get("content-type")).toBe("image/png");
    expect(r.headers.get("etag")).toBe('"abc"');
    expect(r.headers.get("cache-control")).toBe("public, max-age=60");
  });

  it("un 304 se devuelve sin cuerpo", async () => {
    const fetchImpl = vi.fn(async () => new Response(null, { status: 304, headers: { etag: '"abc"' } }));
    const r = await reenviarAlBff(new Request("https://portal/api/v1/persona-natural/configuracion/banners/b1/imagen"), ["configuracion", "banners", "b1", "imagen"], { apiOrigin: ORIGEN, fetchImpl });
    expect(r.status).toBe(304);
    expect(r.cuerpo).toBeNull();
  });

  it("fuera de /configuracion no reenvía If-None-Match ni Cache-Control", async () => {
    const fetchImpl = vi.fn(async () => new Response("{}", { status: 200, headers: { "content-type": "application/json", "cache-control": "no-store" } }));
    const r = await reenviarAlBff(new Request("https://portal/api/v1/persona-natural/organismos", { headers: { "if-none-match": "x" } }), ["organismos"], { apiOrigin: ORIGEN, fetchImpl });
    const [, init] = fetchImpl.mock.calls[0] as unknown as [URL, RequestInit];
    expect((init.headers as Headers).get("if-none-match")).toBeNull();
    expect(r.headers.get("cache-control")).toBeNull();
  });
});
