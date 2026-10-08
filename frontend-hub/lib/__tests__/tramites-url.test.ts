import { describe, expect, it } from "vitest";
import { tramitesUrlFor, tramitesUrls } from "../tramites-url";

// Raíces alternativas (PDN: flitsas.online y app.flitsas.com): el hub manda a la Trámites de la raíz por la que se entró.
const PDN = {
  TRAMITES_URL: "https://tramites.flitsas.online/",
  TRAMITES_URLS: "https://tramites.flitsas.com, https://tramites.flitsas.online",
} as unknown as NodeJS.ProcessEnv;

describe("tramitesUrlFor", () => {
  it("lista cerrada: la principal primero, sin repetir ni barras finales", () => {
    expect(tramitesUrls(PDN)).toEqual(["https://tramites.flitsas.online", "https://tramites.flitsas.com"]);
  });

  it.each([
    ["app.flitsas.com", "https://tramites.flitsas.com"],
    ["app.flitsas.com:443", "https://tramites.flitsas.com"],
    ["flitsas.online", "https://tramites.flitsas.online"],
    ["evil.example.com", "https://tramites.flitsas.online"],
    [null, "https://tramites.flitsas.online"],
  ])("%s → %s", (host, expected) => {
    expect(tramitesUrlFor(host, PDN)).toBe(expected);
  });

  it("sin raíces alternativas (DEV/QA) es siempre TRAMITES_URL", () => {
    const dev = { TRAMITES_URL: "https://dev.tramites.flitsas.online" } as unknown as NodeJS.ProcessEnv;
    expect(tramitesUrlFor("app.flitsas.com", dev)).toBe("https://dev.tramites.flitsas.online");
  });
});
