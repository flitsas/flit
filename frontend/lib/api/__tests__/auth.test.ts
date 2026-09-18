// HU #12424 AC5 — paridad del dominio de FLIT: las llamadas de `loginUser`/`forgotPassword`
// (URL, método, cuerpo) deben seguir siendo IDÉNTICAS a las de hoy. `network` es aditivo y
// opcional en la respuesta — su sola presencia en el tipo no debe alterar la petición ni romper
// el flujo cuando el backend no lo envía (host FLIT).
//
// Uso de ejemplo:
//   const result = await loginUser("a@b.com", "secret"); // → { accessToken, ... , network? }
import { beforeEach, afterEach, describe, expect, it, vi } from "vitest";
import { forgotPassword, loginUser } from "../auth";

const originalFetch = global.fetch;

beforeEach(() => {
  vi.clearAllMocks();
  document.cookie = "flit_token=; path=/; Max-Age=0";
  window.localStorage.clear();
});

afterEach(() => {
  global.fetch = originalFetch;
});

function mockFetchOnce(status: number, body: unknown): ReturnType<typeof vi.fn> {
  const text = body === null ? "" : JSON.stringify(body);
  const fetchMock = vi.fn().mockResolvedValue(
    new Response(text, { status, headers: { "Content-Type": "application/json" } }),
  );
  global.fetch = fetchMock as never;
  return fetchMock;
}

describe("loginUser — contrato sin cambios (HU #12424 AC5)", () => {
  it("POST /api/v1/auth/login con { email, password } — misma URL, método y cuerpo de siempre", async () => {
    const fetchMock = mockFetchOnce(200, { accessToken: "jwt.abc", expiresInSeconds: 43200, tokenType: "Bearer" });

    const result = await loginUser("admin@flit.io", "Secret123");

    expect(fetchMock).toHaveBeenCalledOnce();
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toContain("/api/v1/auth/login");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body as string)).toEqual({ email: "admin@flit.io", password: "Secret123" });
    expect(result).toEqual({ accessToken: "jwt.abc", expiresInSeconds: 43200, tokenType: "Bearer" });
  });

  it("host FLIT sin campo `network` en la respuesta — el resultado no exige ni infiere nada extra (AC5/AC7)", async () => {
    mockFetchOnce(200, { accessToken: "jwt.abc", expiresInSeconds: 43200, tokenType: "Bearer" });
    const result = await loginUser("admin@flit.io", "Secret123");
    expect(result.network).toBeUndefined();
  });

  it("campo `network` aditivo: si el backend lo envía (host de red), se propaga sin transformarlo", async () => {
    mockFetchOnce(200, {
      accessToken: "jwt.abc",
      expiresInSeconds: 43200,
      tokenType: "Bearer",
      network: { host: "app.movilidadandina.com" },
    });
    const result = await loginUser("user@red.io", "Secret123");
    expect(result.network).toEqual({ host: "app.movilidadandina.com" });
  });
});

describe("forgotPassword — contrato sin cambios (HU #12424 AC4)", () => {
  it("POST /api/v1/auth/forgot-password con { email } — misma URL, método y cuerpo de siempre", async () => {
    const fetchMock = mockFetchOnce(202, null);

    await forgotPassword("demo@flit.local");

    expect(fetchMock).toHaveBeenCalledOnce();
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toContain("/api/v1/auth/forgot-password");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body as string)).toEqual({ email: "demo@flit.local" });
  });
});
