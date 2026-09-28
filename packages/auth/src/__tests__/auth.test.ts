import { afterEach, describe, expect, it, vi } from "vitest";
import { chunkedCookies, parseCookies, readChunked, SESSION_COOKIE, TX_COOKIE } from "../cookies";
import { base64UrlEncode, pkceChallenge, seal, unseal } from "../crypto";
import { createApiProxy } from "../proxy";
import { createAuthRoutes, safeReturnTo } from "../routes";
import { sessionUser } from "../claims";
import type { AuthConfig } from "../config";
import { pack, unsealSession } from "../store";
import type { StoredSession } from "../types";

// A-09 (HU del Feature #12887) — @flit/auth: login OIDC con PKCE, sesión cifrada en cookie HttpOnly sin Domain y
// proxy a la API con el Bearer de la sesión.

const config: AuthConfig = {
  productCode: "tramites",
  hubUrl: "https://dev.flitsas.online",
  oidcInternalUrl: "http://gateway:4002",
  apiOrigin: "http://gateway:4002",
  sessionSecret: "s".repeat(40),
  internalApiKey: "clave",
  appUrl: undefined,
};
const routes = createAuthRoutes({ productCode: "tramites", config: () => config });
const APP = "https://dev.tramites.flitsas.online";

afterEach(() => vi.unstubAllGlobals());

function jwt(claims: Record<string, unknown>): string {
  const enc = (o: unknown) => base64UrlEncode(new TextEncoder().encode(JSON.stringify(o)));
  return `${enc({ alg: "RS256" })}.${enc(claims)}.firma`;
}

function cookieHeader(setCookies: string[]): string {
  return setCookies.map((c) => c.split(";")[0]).filter((c) => !c.endsWith("=")).join("; ");
}

async function sessionCookieHeader(session: StoredSession): Promise<string> {
  return cookieHeader(chunkedCookies(SESSION_COOKIE, await seal(pack(session), config.sessionSecret), { secure: true, maxAgeSeconds: 60 }));
}

const now = () => Math.floor(Date.now() / 1000);

describe("cifrado y cookies", () => {
  it("lo cifrado solo se abre con el mismo secreto", async () => {
    const sealed = await seal({ a: 1 }, config.sessionSecret);
    expect(await unseal(sealed, config.sessionSecret)).toEqual({ a: 1 });
    expect(await unseal(sealed, "otro-secreto-de-al-menos-32-caracteres!!")).toBeNull();
    expect(await unseal(sealed.slice(0, -2) + "AA", config.sessionSecret)).toBeNull();
  });

  it("comprimida, la sesión de un SuperAdmin con 150 permisos cabe en una sola cookie", async () => {
    const permissions = Array.from({ length: 150 }, (_, i) => `tramites.modulo${i % 30}.accion${i % 6}`);
    const accessToken = jwt({ sub: "0192f4c1-0000-7000-8000-000000000001", aud: "tramites", permissions, roles: [{ id: "x", code: "SuperAdmin" }], exp: now() + 900 });
    const session = { accessToken, refreshToken: "r".repeat(43), expiresAt: now() + 900 };
    const sealed = await seal(pack(session), config.sessionSecret);

    expect(await unsealSession(sealed, config.sessionSecret)).toEqual(session);

    expect(accessToken.length).toBeGreaterThan(4500);
    expect(sealed.length).toBeLessThan(2000);
  });

  it("una sesión grande se parte en trozos y se vuelve a unir", () => {
    const value = "x".repeat(9000);
    const headers = chunkedCookies(SESSION_COOKIE, value, { secure: true, maxAgeSeconds: 60 });
    expect(readChunked(parseCookies(cookieHeader(headers)), SESSION_COOKIE)).toBe(value);
  });

  it("ninguna cookie lleva Domain y todas son HttpOnly y SameSite=Lax", () => {
    for (const header of chunkedCookies(SESSION_COOKIE, "x".repeat(5000), { secure: true, maxAgeSeconds: 60 })) {
      expect(header.toLowerCase()).not.toContain("domain=");
      expect(header).toContain("HttpOnly");
      expect(header).toContain("SameSite=Lax");
    }
  });

  it("solo acepta retornos relativos de la misma app", () => {
    expect(safeReturnTo("/tramites?x=1")).toBe("/tramites?x=1");
    expect(safeReturnTo("https://evil.example")).toBe("/");
    expect(safeReturnTo("//evil.example")).toBe("/");
    expect(safeReturnTo(null)).toBe("/");
  });
});

describe("login y callback", () => {
  it("login lleva al authorize del hub con PKCE y guarda la transacción cifrada", async () => {
    const response = await routes.login(new Request(`${APP}/auth/login?returnTo=%2Ftramites`));

    expect(response.status).toBe(302);
    const location = new URL(response.headers.get("location")!);
    expect(location.origin + location.pathname).toBe("https://dev.flitsas.online/connect/authorize");
    expect(location.searchParams.get("client_id")).toBe("tramites");
    expect(location.searchParams.get("redirect_uri")).toBe(`${APP}/auth/callback`);
    expect(location.searchParams.get("code_challenge_method")).toBe("S256");
    const tx = response.headers.getSetCookie().find((c) => c.startsWith(`${TX_COOKIE}=`))!;
    expect(tx).toContain("HttpOnly");
    expect(tx.toLowerCase()).not.toContain("domain=");
  });

  it("callback canjea el código con el verificador, abre la sesión y vuelve a donde estaba", async () => {
    const login = await routes.login(new Request(`${APP}/auth/login?returnTo=%2Ftramites`));
    const state = new URL(login.headers.get("location")!).searchParams.get("state")!;
    const challenge = new URL(login.headers.get("location")!).searchParams.get("code_challenge")!;
    const accessToken = jwt({ sub: "u1", aud: "tramites", exp: now() + 900 });
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ access_token: accessToken, refresh_token: "r1", expires_in: 900 }));
    vi.stubGlobal("fetch", fetchMock);

    const response = await routes.callback(new Request(`${APP}/auth/callback?code=c1&state=${state}`, {
      headers: { cookie: cookieHeader(login.headers.getSetCookie()) },
    }));

    expect(response.status).toBe(302);
    expect(response.headers.get("location")).toBe("/tramites");
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://gateway:4002/connect/token");
    const form = new URLSearchParams(init.body as string);
    expect(form.get("code")).toBe("c1");
    expect(await pkceChallenge(form.get("code_verifier")!)).toBe(challenge);
    expect((init.headers as Record<string, string>)["x-flit-domain"]).toBe("dev.flitsas.online");

    const session = await unsealSession(readChunked(parseCookies(cookieHeader(response.headers.getSetCookie())), SESSION_COOKIE)!, config.sessionSecret);
    expect(session).toMatchObject({ accessToken, refreshToken: "r1" });
  });

  it("un callback con state ajeno no abre sesión", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const response = await routes.callback(new Request(`${APP}/auth/callback?code=c1&state=otro`));

    expect(response.headers.get("location")).toBe("/auth/login");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("si el hub niega el acceso, va a la página de error con el código", async () => {
    const login = await routes.login(new Request(`${APP}/auth/login`));
    const state = new URL(login.headers.get("location")!).searchParams.get("state")!;

    const response = await routes.callback(new Request(`${APP}/auth/callback?error=access_denied&error_description=PRODUCT_ROLE_REQUIRED&state=${state}`, {
      headers: { cookie: cookieHeader(login.headers.getSetCookie()) },
    }));

    expect(response.headers.get("location")).toBe("/403?code=PRODUCT_ROLE_REQUIRED");
  });
});

describe("renovación y proxy", () => {
  it("el proxy pone el Bearer de la sesión y descarta la cookie y los sellos del navegador", async () => {
    const accessToken = jwt({ sub: "u1", exp: now() + 900 });
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ ok: true }));
    vi.stubGlobal("fetch", fetchMock);
    const proxy = createApiProxy({ productCode: "tramites", config: () => config });

    const response = await proxy(new Request(`${APP}/api/v1/tramites?x=1`, {
      headers: { host: "dev.tramites.flitsas.online", cookie: await sessionCookieHeader({ accessToken, refreshToken: "r1", expiresAt: now() + 900 }), "x-flit-domain": "otra-red.com", authorization: "Bearer robado" },
    }), ["tramites"]);

    expect(response.status).toBe(200);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://gateway:4002/api/v1/tramites?x=1");
    const headers = init.headers as Headers;
    expect(headers.get("authorization")).toBe(`Bearer ${accessToken}`);
    expect(headers.get("x-flit-domain")).toBe("dev.tramites.flitsas.online");
    expect(headers.has("cookie")).toBe(false);
  });

  it("con el token por vencer lo renueva, lo usa y guarda la sesión nueva", async () => {
    const renewed = jwt({ sub: "u1", exp: now() + 900 });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(Response.json({ access_token: renewed, refresh_token: "r2", expires_in: 900 }))
      .mockResolvedValueOnce(Response.json({ ok: true }));
    vi.stubGlobal("fetch", fetchMock);
    const proxy = createApiProxy({ productCode: "tramites", config: () => config });

    const response = await proxy(new Request(`${APP}/api/v1/x`, {
      headers: { host: "dev.tramites.flitsas.online", cookie: await sessionCookieHeader({ accessToken: jwt({ exp: now() + 10 }), refreshToken: "r1", expiresAt: now() + 10 }) },
    }), ["x"]);

    expect(new URLSearchParams(fetchMock.mock.calls[0][1].body as string).get("refresh_token")).toBe("r1");
    expect((fetchMock.mock.calls[1][1].headers as Headers).get("authorization")).toBe(`Bearer ${renewed}`);
    const stored = await unsealSession(readChunked(parseCookies(cookieHeader(response.headers.getSetCookie())), SESSION_COOKIE)!, config.sessionSecret);
    expect(stored?.refreshToken).toBe("r2");
  });

  it("si el refresh ya no sirve, borra la sesión y responde SESSION_EXPIRED", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json({ error: "invalid_grant", error_description: "PRODUCT_ROLE_REQUIRED" }, { status: 400 })));
    const proxy = createApiProxy({ productCode: "tramites", config: () => config });

    const response = await proxy(new Request(`${APP}/api/v1/x`, {
      headers: { host: "dev.tramites.flitsas.online", cookie: await sessionCookieHeader({ accessToken: jwt({}), refreshToken: "r1", expiresAt: now() - 5 }) },
    }), ["x"]);

    expect(response.status).toBe(401);
    expect(await response.json()).toMatchObject({ code: "SESSION_EXPIRED" });
    expect(response.headers.getSetCookie().some((c) => c.startsWith(`${SESSION_COOKIE}=;`) && c.includes("Max-Age=0"))).toBe(true);
  });

  it("sin sesión el proxy no llama a la API", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    const proxy = createApiProxy({ productCode: "tramites", config: () => config });

    const response = await proxy(new Request(`${APP}/api/v1/x`), ["x"]);

    expect(response.status).toBe(401);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("proxy anónimo", () => {
  it("con allowAnonymous reenvía sin Bearer cuando no hay sesión", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 202 }));
    vi.stubGlobal("fetch", fetchMock);
    const proxy = createApiProxy({ productCode: "plataforma", allowAnonymous: true, config: () => config });

    const response = await proxy(new Request(`${APP}/api/v1/auth/forgot-password`, { method: "POST", body: "{}", headers: { host: "dev.flitsas.online" } }), ["auth", "forgot-password"]);

    expect(response.status).toBe(202);
    expect((fetchMock.mock.calls[0][1].headers as Headers).has("authorization")).toBe(false);
  });
});

describe("SessionUser", () => {
  it("sale de los claims del token del producto, con la regla multi-rol del SuperAdmin", () => {
    const user = sessionUser(jwt({
      sub: "u1", email: "a@b.co", aud: "tramites", dom: "flit", tenant_id: "t1", tenant_name: "Empresa", company_nit: "900",
      tenant_type: "RENTING", entity_type: "COMPANY", is_group_parent: false, exp: 123,
      roles: [{ id: "r1", code: "Radicador" }, { id: "r2", code: "SuperAdmin" }], role: ["Radicador", "SuperAdmin"], permissions: ["tramites.read"],
    }));

    expect(user).toMatchObject({ id: "u1", product: "tramites", domain: "flit", permissions: ["tramites.read"], isSuperAdmin: true, expiresAt: 123 });
    expect(user.tenant).toEqual({ id: "t1", name: "Empresa", nit: "900", type: "RENTING", entityType: "COMPANY", parentId: null, isGroupParent: false });
  });
});
