import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useAuthGate } from "../useAuthGate";

const replace = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/tramites/123",
}));

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

describe("useAuthGate", () => {
  beforeEach(() => {
    replace.mockClear();
    document.cookie = "flit_token=; path=/; Max-Age=0";
  });

  it("navega a /login con returnUrl cuando no hay token", async () => {
    const { result } = renderHook(() => useAuthGate());

    await waitFor(() => expect(result.current.hydrated).toBe(true));
    expect(result.current.authed).toBe(false);
    expect(replace).toHaveBeenCalledWith("/login?returnUrl=%2Ftramites%2F123");
  });

  it("navega a /login y limpia el token cuando ya expiró", async () => {
    const past = Math.floor(Date.now() / 1000) - 3600;
    window.localStorage.setItem("flit:jwt", makeToken({ sub: "u1", exp: past }));

    const { result } = renderHook(() => useAuthGate());

    await waitFor(() => expect(result.current.hydrated).toBe(true));
    expect(result.current.authed).toBe(false);
    expect(replace).toHaveBeenCalledWith("/login?returnUrl=%2Ftramites%2F123");
    expect(window.localStorage.getItem("flit:jwt")).toBeNull();
  });

  it("no redirige cuando el token está vigente", async () => {
    const future = Math.floor(Date.now() / 1000) + 3600;
    window.localStorage.setItem("flit:jwt", makeToken({ sub: "u1", exp: future }));

    const { result } = renderHook(() => useAuthGate());

    await waitFor(() => expect(result.current.hydrated).toBe(true));
    expect(result.current.authed).toBe(true);
    expect(replace).not.toHaveBeenCalled();
  });

  it("logout limpia el token y navega a /login sin returnUrl", async () => {
    const future = Math.floor(Date.now() / 1000) + 3600;
    window.localStorage.setItem("flit:jwt", makeToken({ sub: "u1", exp: future }));

    const { result } = renderHook(() => useAuthGate());
    await waitFor(() => expect(result.current.hydrated).toBe(true));

    result.current.logout();

    expect(replace).toHaveBeenCalledWith("/login");
    expect(window.localStorage.getItem("flit:jwt")).toBeNull();
  });
});
