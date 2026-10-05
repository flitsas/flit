import { afterEach, describe, expect, it, vi } from "vitest";
import { SESSION_EXPIRED_EVENT } from "@/lib/auth/session";

// HU #13004 — mientras la app va al hub por una sesión nueva (la página está por recargarse), las llamadas no fallan
// ni salen: quedan esperando, así ninguna pantalla pinta «Tu sesión expiró» un instante antes de recargar.

const state = { reauthenticating: false };
vi.mock("@flit/auth/client", () => ({ isReauthenticating: () => state.reauthenticating }));

import { apiFetch } from "../client";

const originalFetch = global.fetch;
afterEach(() => {
  global.fetch = originalFetch;
  state.reauthenticating = false;
});

/** `true` si la promesa sigue pendiente tras dejar correr las microtareas. */
async function isPending(promise: Promise<unknown>): Promise<boolean> {
  const marker = Symbol("pending");
  return (await Promise.race([promise.catch(() => "rejected"), new Promise((r) => setTimeout(() => r(marker), 20))])) === marker;
}

describe("apiFetch durante la reconexión", () => {
  it("si ya se está reconectando, no llama a la API y la llamada queda esperando", async () => {
    state.reauthenticating = true;
    const fetchMock = vi.fn();
    global.fetch = fetchMock as typeof fetch;

    expect(await isPending(apiFetch("/api/v1/analytics/overview"))).toBe(true);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("un 401 SESSION_EXPIRED que dispara la reconexión deja la llamada esperando en vez de fallar", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: "SESSION_EXPIRED" }), { status: 401 })) as typeof fetch;
    const onExpired = () => (state.reauthenticating = true);
    window.addEventListener(SESSION_EXPIRED_EVENT, onExpired);

    expect(await isPending(apiFetch("/api/v1/security/modules"))).toBe(true);

    window.removeEventListener(SESSION_EXPIRED_EVENT, onExpired);
  });
});
