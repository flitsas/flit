import { describe, expect, it, vi } from "vitest";

vi.mock("server-only", () => ({}));

import { hubConfig } from "../config.server";

describe("hubConfig", () => {
  it("sin variables usa los valores locales", () => {
    expect(hubConfig({} as NodeJS.ProcessEnv)).toEqual({
      apiOrigin: "http://localhost:4002",
      internalApiKey: undefined,
      loginUrl: "/auth/login",
    });
  });

  it("lee cada ambiente de sus variables, sin barras finales", () => {
    const config = hubConfig({
      CORE_API_ORIGIN: "http://gateway:4002/",
      FLIT_INTERNAL_API_KEY: "k",
    } as unknown as NodeJS.ProcessEnv);

    expect(config).toEqual({
      apiOrigin: "http://gateway:4002",
      internalApiKey: "k",
      loginUrl: "/auth/login",
    });
  });

  it("HUB_LOGIN_URL cambia adónde lleva «Iniciar sesión»", () => {
    expect(hubConfig({ HUB_LOGIN_URL: "https://otro/login" } as unknown as NodeJS.ProcessEnv).loginUrl).toBe("https://otro/login");
  });
});
