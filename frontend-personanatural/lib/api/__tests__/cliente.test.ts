import { describe, expect, it, vi } from "vitest";
import { crearCliente } from "../cliente";
import { ApiError, esperaDe, mensajeDe, MENSAJE_GENERICO } from "../errores";

const json = (cuerpo: unknown, init: ResponseInit = {}) =>
  new Response(JSON.stringify(cuerpo), { status: 200, headers: { "content-type": "application/json" }, ...init });
const problema = (status: number, code: string, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify({ type: "about:blank", title: "Titulo del back", status, code }), {
    status,
    headers: { "content-type": "application/problem+json", ...headers },
  });

describe("cliente del BFF", () => {
  it("llama por recurso al prefijo del portal, sin conocer ninguna URL de FLIT 2", async () => {
    const fetchImpl = vi.fn(async () => json([{ id: "1", nombre: "Medellín" }]));
    const cliente = crearCliente({ fetchImpl });

    const organismos = await cliente.catalogos.organismos();

    expect(organismos).toEqual([{ id: "1", nombre: "Medellín" }]);
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/v1/persona-natural/organismos");
    expect(url).not.toMatch(/^https?:/);
    expect(init.credentials).toBe("same-origin");
  });

  it("manda el captcha y la llave de idempotencia en sus cabeceras y el cuerpo como JSON", async () => {
    const fetchImpl = vi.fn(async () => json({ solicitudId: "s1", estado: "en_cola" }, { status: 202 }));
    const cliente = crearCliente({ fetchImpl });

    await cliente.solicitudes.radicar("s1", "llave-123");
    await cliente.sesiones.reanudar({ radicado: "PN-1", cedula: "123" }, { captcha: "tk" });

    const [, radicar] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    const [, reanudar] = fetchImpl.mock.calls[1] as unknown as [string, RequestInit];
    expect((radicar.headers as Record<string, string>)["idempotency-key"]).toBe("llave-123");
    expect((reanudar.headers as Record<string, string>)["x-captcha-token"]).toBe("tk");
    expect(reanudar.body).toBe(JSON.stringify({ radicado: "PN-1", cedula: "123" }));
  });

  it("codifica el id de la solicitud en la ruta", async () => {
    const fetchImpl = vi.fn(async () => json({}));
    await crearCliente({ fetchImpl }).solicitudes.obtener("a/b?c");

    expect((fetchImpl.mock.calls[0] as unknown as [string])[0]).toBe("/api/v1/persona-natural/solicitudes/a%2Fb%3Fc");
  });

  it("convierte un problem+json en un error tipado con código, título y estado", async () => {
    const cliente = crearCliente({ fetchImpl: async () => problema(400, "captcha_required") });

    const error = await cliente.catalogos.tiposTramite().catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ code: "captcha_required", title: "Titulo del back", status: 400 });
  });

  it("ante un 429 no reintenta: una sola llamada y el error trae la espera", async () => {
    const fetchImpl = vi.fn(async () => problema(429, "rate_limited", { "retry-after": "42" }));
    const cliente = crearCliente({ fetchImpl });

    const error = await cliente.catalogos.organismos().catch((e: unknown) => e);

    expect(fetchImpl).toHaveBeenCalledTimes(1);
    expect(esperaDe(error)).toBe(42);
    expect(mensajeDe(error)).toBe("Demasiados intentos. Vuelve a intentarlo en unos minutos.");
  });

  it("un cuerpo que no es JSON igual produce un error con el código por estado", async () => {
    const cliente = crearCliente({ fetchImpl: async () => new Response("<html>", { status: 502 }) });

    await expect(cliente.catalogos.organismos()).rejects.toMatchObject({ code: "http_502", status: 502 });
  });

  it("un 204 no intenta leer cuerpo", async () => {
    const cliente = crearCliente({ fetchImpl: async () => new Response(null, { status: 204 }) });

    await expect(cliente.solicitudes.cancelar("s1")).resolves.toBeUndefined();
  });
});

describe("mensajes para el ciudadano", () => {
  it.each([
    [new ApiError("captcha_required", "x", 400), "Resuelve la verificación"],
    [new ApiError("otro", "x", 401), "inicies o reanudes"],
    [new ApiError("otro", "x", 403), "No tienes acceso"],
    [new ApiError("otro", "x", 409), "cambió mientras"],
    [new ApiError("otro", "x", 422), "no son válidos"],
    [new ApiError("otro", "x", 429), "Demasiados intentos"],
    [new ApiError("otro", "x", 503), "no está disponible"],
    [new ApiError("session_expired", "x", 401), "Tu sesión venció"],
  ])("%#: tiene mensaje propio en español", (error, fragmento) => {
    expect(mensajeDe(error)).toContain(fragmento);
  });

  it("nunca expone detalles técnicos: un error cualquiera da el mensaje genérico", () => {
    expect(mensajeDe(new Error("NullReference en SesionService"))).toBe(MENSAJE_GENERICO);
    expect(mensajeDe(new ApiError("raro", "Stack trace", 418))).toBe(MENSAJE_GENERICO);
  });

  it("el error tipado solo guarda código, título y estado: ni cuerpo ni datos personales", () => {
    const error = new ApiError("x", "t", 400);

    expect(Object.keys(error).sort()).toEqual(["code", "name", "reintentarEnSegundos", "status", "title"]);
  });
});
