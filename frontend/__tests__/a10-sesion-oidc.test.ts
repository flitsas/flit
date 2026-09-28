import { afterEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";
import { resolveApiBase, sessionAwareBase } from "@/lib/api/base-url";
import { middleware } from "@/middleware";

// A-10 (HU #13001) — Trámites con la sesión de @flit/auth detrás de FLIT_SESSION_MODE. Con la sesión antigua (por
// defecto) todo queda igual; con la nueva la API se llama por el mismo origen y /login lleva al hub.

const API = "https://api.dev.flitsas.online/api/v1";

afterEach(() => {
  document.documentElement.removeAttribute("data-session-mode");
  vi.unstubAllEnvs();
});

describe("base de la API", () => {
  it("con la sesión antigua, la base configurada de siempre", () => {
    document.documentElement.setAttribute("data-session-mode", "legacy");
    expect(sessionAwareBase(API)).toBe(API);
  });

  it("con la sesión de @flit/auth, siempre el mismo origen (BFF)", () => {
    document.documentElement.setAttribute("data-session-mode", "oidc");
    expect(sessionAwareBase(API)).toBe("");
    expect(resolveApiBase(API)).toBe("");
  });
});

describe("middleware", () => {
  const request = (path: string, cookie?: string) =>
    new NextRequest(new URL(path, "https://dev.flitsas.online"), cookie ? { headers: { cookie } } : undefined);

  it("con la sesión antigua, /api/v1 sigue por los rewrites y /login no cambia", async () => {
    const api = await middleware(request("/api/v1/tramites"));
    expect(api.headers.get("x-middleware-rewrite")).toBeNull();
    expect(api.headers.get("location")).toBeNull();

    const login = await middleware(request("/login"));
    expect(login.headers.get("location")).toBeNull();
  });

  it("con la sesión de @flit/auth, /api/v1 va al BFF conservando la consulta", async () => {
    vi.stubEnv("FLIT_SESSION_MODE", "oidc");
    vi.stubEnv("FLIT_SESSION_SECRET", "s".repeat(40));

    const response = await middleware(request("/api/v1/tramites?page=2"));

    expect(response.headers.get("x-middleware-rewrite")).toBe("https://dev.flitsas.online/bff/api/v1/tramites?page=2");
  });

  it("con la sesión de @flit/auth, /login lleva al login del hub conservando el retorno", async () => {
    vi.stubEnv("FLIT_SESSION_MODE", "oidc");
    vi.stubEnv("FLIT_SESSION_SECRET", "s".repeat(40));

    const response = await middleware(request("/login?returnUrl=%2Ftramites"));

    expect(response.headers.get("location")).toBe("https://dev.flitsas.online/auth/login?returnTo=%2Ftramites");
  });

  it("con la sesión de @flit/auth y sin sesión, /admin no deja pasar", async () => {
    vi.stubEnv("FLIT_SESSION_MODE", "oidc");
    vi.stubEnv("FLIT_SESSION_SECRET", "s".repeat(40));

    const response = await middleware(request("/admin/plataforma"));

    expect(response.headers.get("location")).not.toBeNull();
  });
});
