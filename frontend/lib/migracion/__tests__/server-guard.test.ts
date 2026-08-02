import { beforeEach, describe, expect, it, vi } from "vitest";

/**
 * El BFF de migración es la ÚNICA cosa que separa la llave maestra del host —la que reescribe
 * trámites de producción sin tenant ni dueño— del navegador. Merece tests propios.
 *
 * `next/headers` y `server-only` se sustituyen porque ninguno existe fuera del runtime de Next.
 */
const cookieStore = { valor: undefined as string | undefined };

vi.mock("server-only", () => ({}));
vi.mock("next/headers", () => ({
  cookies: () =>
    Promise.resolve({
      get: (nombre: string) =>
        nombre === "flit_token" && cookieStore.valor !== undefined
          ? { name: nombre, value: cookieStore.valor }
          : undefined,
    }),
}));

const { exigirSuperAdmin, llamarMigracion } = await import("@/lib/migracion/server");

/** Un JWT sin firmar: el frontend solo lee claims, nunca verifica (igual que `lib/auth/jwt`). */
function token(payload: Record<string, unknown>): string {
  const b64 = (o: unknown) =>
    Buffer.from(JSON.stringify(o))
      .toString("base64")
      .replace(/\+/g, "-")
      .replace(/\//g, "_")
      .replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64(payload)}.`;
}

const MANANA = Math.floor(Date.now() / 1000) + 3600;
const AYER = Math.floor(Date.now() / 1000) - 3600;

describe("exigirSuperAdmin", () => {
  beforeEach(() => {
    cookieStore.valor = undefined;
  });

  it("deja pasar a un SuperAdmin con sesión vigente", async () => {
    cookieStore.valor = token({ role: "SuperAdmin", exp: MANANA });

    expect(await exigirSuperAdmin()).toBeNull();
  });

  it("rechaza cuando no hay cookie", async () => {
    const rechazo = await exigirSuperAdmin();

    expect(rechazo?.estado).toBe(403);
  });

  it("rechaza un token malformado sin reventar", async () => {
    cookieStore.valor = "esto-no-es-un-jwt";

    expect((await exigirSuperAdmin())?.estado).toBe(403);
  });

  /** El caso que importa: un usuario legítimo de la plataforma NO es operador de migración. */
  it.each(["AdminCompany", "ot_admin", "user"])("rechaza el rol %s", async (role) => {
    cookieStore.valor = token({ role, exp: MANANA });

    expect((await exigirSuperAdmin())?.estado).toBe(403);
  });

  it("rechaza a un SuperAdmin con la sesión expirada", async () => {
    cookieStore.valor = token({ role: "SuperAdmin", exp: AYER });

    expect((await exigirSuperAdmin())?.estado).toBe(401);
  });

  /** Multi-rol (HU #10506): el rol puede venir en el arreglo `roles`, no en `role`. */
  it("reconoce SuperAdmin en el arreglo roles", async () => {
    cookieStore.valor = token({ roles: [{ code: "SuperAdmin" }], exp: MANANA });

    expect(await exigirSuperAdmin()).toBeNull();
  });
});

describe("llamarMigracion", () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
  });

  /**
   * Fail-closed: sin llave configurada NO se llama al host. Lo contrario —llamar sin cabecera y
   * dejar que el host responda 401— dejaría un 401 imposible de distinguir de una llave mal puesta.
   */
  it("sin llave configurada no llama al host", async () => {
    const fetchSpy = vi.fn();
    vi.stubGlobal("fetch", fetchSpy);

    const respuesta = await llamarMigracion("/api/v1/migracion/estado/registration?ids=1", {
      method: "GET",
    });

    expect(fetchSpy).not.toHaveBeenCalled();
    expect(respuesta.estado).toBe(503);
  });

  it("traduce un host caído en 502 y no en una excepción", async () => {
    vi.stubEnv("MIGRACION_API_KEY", "llave");
    vi.stubGlobal(
      "fetch",
      vi.fn().mockRejectedValue(new Error("ECONNREFUSED")),
    );

    const respuesta = await llamarMigracion("/api/v1/migracion/estado/registration?ids=1", {
      method: "GET",
    });

    expect(respuesta.estado).toBe(502);
  });

  /**
   * El caso MÁS probable: `MigracionApi:Enabled` viene apagado por defecto en todos los ambientes,
   * y con él las rutas ni se registran. El host devuelve el 404 pelado de ASP.NET, sin cuerpo, y
   * sin este código la consola caía en su mensaje genérico —«La migración no se pudo lanzar. El
   * servidor respondió 404»—, que no dice qué hacer justo cuando la solución es una variable.
   */
  it("un 404 sin cuerpo se traduce a «el migrador está apagado»", async () => {
    vi.stubEnv("MIGRACION_API_KEY", "llave");
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({ status: 404, text: () => Promise.resolve("") }),
    );

    const respuesta = await llamarMigracion("/api/v1/migracion/estado/registration?ids=1", {
      method: "GET",
    });

    expect(respuesta.estado).toBe(404);
    expect((respuesta.cuerpo as { title: string }).title).toBe("migracion.apagado");
  });

  /** Un 404 que el host SÍ explica se respeta tal cual: no todo 404 es el migrador apagado. */
  it("no pisa un 404 que viene explicado", async () => {
    vi.stubEnv("MIGRACION_API_KEY", "llave");
    const cuerpo = JSON.stringify({ title: "migracion.otra_cosa", detail: "…", status: 404 });
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({ status: 404, text: () => Promise.resolve(cuerpo) }),
    );

    const respuesta = await llamarMigracion("/api/v1/migracion/estado/registration?ids=1", {
      method: "GET",
    });

    expect((respuesta.cuerpo as { title: string }).title).toBe("migracion.otra_cosa");
  });
});
