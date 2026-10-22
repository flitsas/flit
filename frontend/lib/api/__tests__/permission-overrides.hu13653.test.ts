// HU #13653 — el PUT envía piiConfirmed solo cuando el administrador confirmó la Ley 1581.
import { afterEach, describe, expect, it, vi } from "vitest";
import { setUserOverride } from "../permission-overrides";

const originalFetch = global.fetch;
afterEach(() => {
  global.fetch = originalFetch;
});

function mockOk() {
  const fn = vi.fn().mockResolvedValue(
    new Response(JSON.stringify({ permissionId: "p", effect: "allow", changed: true }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    }),
  );
  global.fetch = fn;
  return fn;
}

const bodyOf = (fn: ReturnType<typeof vi.fn>) => JSON.parse(String((fn.mock.calls[0][1] as RequestInit).body));

describe("setUserOverride (HU #13653)", () => {
  it("sin confirmación el cuerpo solo lleva effect", async () => {
    const fn = mockOk();
    await setUserOverride("u1", "p1", "allow", "t1");
    expect(bodyOf(fn)).toEqual({ effect: "allow" });
  });

  it("con confirmación el cuerpo lleva piiConfirmed=true", async () => {
    const fn = mockOk();
    await setUserOverride("u1", "p1", "allow", "t1", true);
    expect(bodyOf(fn)).toEqual({ effect: "allow", piiConfirmed: true });
  });
});
