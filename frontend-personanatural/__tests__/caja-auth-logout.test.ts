// HU #14458 — El cierre de sesión de la caja solo ocurre por POST con Origin propio (un GET cruzado no cierra nada).
import { describe, expect, it, vi } from "vitest";

vi.mock("server-only", () => ({}));
const logout = vi.hoisted(() =>
  vi.fn(async () => new Response(null, { status: 302, headers: { location: "https://hub/connect/logout?client_id=caja", "set-cookie": "flit_session_caja=; Max-Age=0" } })),
);
vi.mock("@/lib/caja/auth.server", () => ({ authRoutes: { logout, login: vi.fn(), callback: vi.fn(), session: vi.fn(), frontchannelLogout: vi.fn(), refresh: vi.fn() } }));

import { GET, POST } from "@/app/auth/[...ruta]/route";

const contexto = (ruta: string[]) => ({ params: Promise.resolve({ ruta }) });

describe("/auth/logout", () => {
  it("un GET (p. ej. <img> de otro sitio) ya no cierra la sesión", async () => {
    logout.mockClear();
    const respuesta = await GET(new Request("https://portal/auth/logout"), contexto(["logout"]));
    expect(respuesta.status).toBe(404);
    expect(logout).not.toHaveBeenCalled();
  });

  it("un POST del propio portal cierra: borra cookies y devuelve la URL de cierre del hub", async () => {
    logout.mockClear();
    const respuesta = await POST(new Request("https://portal/auth/logout", { method: "POST", headers: { origin: "https://portal" } }), contexto(["logout"]));
    expect(respuesta.status).toBe(200);
    expect(await respuesta.json()).toEqual({ redirectTo: "https://hub/connect/logout?client_id=caja" });
    expect(respuesta.headers.getSetCookie().join(";")).toContain("flit_session_caja=");
    expect(logout).toHaveBeenCalledTimes(1);
  });

  it("un POST con otro Origin recibe 403 y no cierra", async () => {
    logout.mockClear();
    const respuesta = await POST(new Request("https://portal/auth/logout", { method: "POST", headers: { origin: "https://malo.example" } }), contexto(["logout"]));
    expect(respuesta.status).toBe(403);
    expect(logout).not.toHaveBeenCalled();
  });
});
