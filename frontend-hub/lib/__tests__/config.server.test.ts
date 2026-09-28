import { describe, expect, it, vi } from "vitest";

vi.mock("server-only", () => ({}));

import { hubConfig } from "../config.server";

describe("hubConfig", () => {
  it("sin variables usa los valores locales", () => {
    expect(hubConfig({} as NodeJS.ProcessEnv)).toEqual({
      apiOrigin: "http://localhost:4002",
      internalApiKey: undefined,
      loginUrl: "http://localhost:3000/login",
    });
  });

  it("lee cada ambiente de sus variables, sin barras finales", () => {
    const config = hubConfig({
      CORE_API_ORIGIN: "http://gateway:4002/",
      FLIT_INTERNAL_API_KEY: "k",
      TRAMITES_URL: "https://dev.tramites.flitsas.online/",
    } as unknown as NodeJS.ProcessEnv);

    expect(config).toEqual({
      apiOrigin: "http://gateway:4002",
      internalApiKey: "k",
      loginUrl: "https://dev.tramites.flitsas.online/login",
    });
  });

  it("HUB_LOGIN_URL gana sobre el login de Trámites", () => {
    expect(hubConfig({ HUB_LOGIN_URL: "/login", TRAMITES_URL: "https://x" } as unknown as NodeJS.ProcessEnv).loginUrl).toBe("/login");
  });
});
