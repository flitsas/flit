// HU #13651 — cliente de GET /api/v1/auth/session-policy (contrato { mode, maxMinutes }).
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../types";
import { fetchSessionPolicy, parseSessionPolicy } from "../session-policy";

const originalFetch = global.fetch;
afterEach(() => {
  global.fetch = originalFetch;
});

function respond(body: unknown, status = 200) {
  global.fetch = vi.fn().mockResolvedValue(
    new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }),
  );
}

describe("fetchSessionPolicy", () => {
  it("pide /api/v1/auth/session-policy y devuelve la política tal cual", async () => {
    respond({ mode: "oidc", maxMinutes: 15 });

    await expect(fetchSessionPolicy()).resolves.toEqual({ mode: "oidc", maxMinutes: 15 });

    const url = String((global.fetch as ReturnType<typeof vi.fn>).mock.calls[0][0]);
    expect(url).toContain("/api/v1/auth/session-policy");
  });

  it("modo heredado: 720 minutos", async () => {
    respond({ mode: "legacy", maxMinutes: 720 });
    await expect(fetchSessionPolicy()).resolves.toEqual({ mode: "legacy", maxMinutes: 720 });
  });

  it("una respuesta con otra forma se rechaza (no llega texto falso a la pantalla)", async () => {
    respond({ mode: "otro", maxMinutes: 15 });
    await expect(fetchSessionPolicy()).rejects.toBeInstanceOf(ApiError);
  });

  it("un 401 se propaga como ApiError", async () => {
    respond({ code: "UNAUTHORIZED" }, 401);
    await expect(fetchSessionPolicy()).rejects.toBeInstanceOf(ApiError);
  });
});

describe("parseSessionPolicy", () => {
  it.each([null, undefined, {}, { mode: "legacy" }, { mode: "legacy", maxMinutes: "720" }, { mode: "oidc", maxMinutes: 0 }, { mode: "oidc", maxMinutes: -1 }])(
    "rechaza %j",
    (raw) => {
      expect(() => parseSessionPolicy(raw)).toThrow(ApiError);
    },
  );
});
