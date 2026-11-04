import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("server-only", () => ({}));
const cabecerasEntrantes = new Headers();
vi.mock("next/headers", () => ({
  cookies: async () => ({ get: () => undefined }),
  headers: async () => cabecerasEntrantes,
}));

import { POST } from "../route";

const sesionDelBff = { token: "tok-secreto-256", expiraEn: "2030-01-01T00:00:00Z", solicitudId: "s1" };

function llamar(path: string[]) {
  const request = new Request(`https://portal/api/v1/persona-natural/${path.join("/")}`, { method: "POST", body: "{}", headers: { "content-type": "application/json" } });
  return POST(request, { params: Promise.resolve({ path }) });
}

describe("ruta del proxy: cookie de sesión", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify(sesionDelBff), { status: 201, headers: { "content-type": "application/json" } })));
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.unstubAllEnvs();
  });

  it("fija pn_sesion HttpOnly, SameSite=Strict y con path acotado al BFF", async () => {
    const respuesta = await llamar(["sesiones"]);
    const cookie = respuesta.headers.get("set-cookie") ?? "";

    expect(cookie).toContain("pn_sesion=tok-secreto-256");
    expect(cookie).toMatch(/HttpOnly/i);
    expect(cookie).toMatch(/SameSite=strict/i);
    expect(cookie).toContain("Path=/api/v1/persona-natural");
    expect(await respuesta.text()).not.toContain("tok-secreto-256");
  });

  it("en producción agrega Secure", async () => {
    vi.stubEnv("NODE_ENV", "production");
    const cookie = (await llamar(["sesiones"])).headers.get("set-cookie") ?? "";
    expect(cookie).toMatch(/;\s*Secure/i);
  });

  it("fuera de producción no la marca Secure (desarrollo sin HTTPS)", async () => {
    vi.stubEnv("NODE_ENV", "test");
    const cookie = (await llamar(["sesiones"])).headers.get("set-cookie") ?? "";
    expect(cookie).not.toMatch(/;\s*Secure/i);
  });
});
