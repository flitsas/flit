import { describe, expect, it } from "vitest";
import { legacyTramitesRedirect } from "../legacy-tramites";

// A-11 (HU #13002): reparto de rutas del plan maestro §4.3.
const TRAMITES = "https://dev.tramites.flitsas.online";
const redirect = (path: string) => legacyTramitesRedirect(new URL(path, "https://dev.flitsas.online"), TRAMITES);

describe("legacyTramitesRedirect", () => {
  it.each([
    "/", "/?m=dashboard", "/login?returnUrl=%2F", "/auth/login", "/auth/reset-password?token=abc", "/reset-password?token=abc",
    "/invite/activate?token=abc", "/403?code=X", "/connect/authorize?client_id=tramites", "/.well-known/jwks.json",
    "/api/v1/platform/me/apps", "/healthz", "/email-assets/flit-logo.png", "/icon.svg",
    "/proximamente/comparendos", "/proximamente/diagnostico",
  ])("%s se queda en el hub", (path) => {
    expect(redirect(path)).toBeNull();
  });

  it.each([
    ["/?m=tramites", "/?m=tramites"],
    ["/tramites/123?tab=docs", "/tramites/123?tab=docs"],
    ["/portal/tok-1", "/portal/tok-1"],
    ["/biometric/tok-2", "/biometric/tok-2"],
    ["/log-qx/abc", "/log-qx/abc"],
    ["/manual/guia", "/manual/guia"],
    ["/admin/transit-offices", "/admin/transit-offices"],
    ["/profile/change-password", "/profile/change-password"],
    ["/empresa/configuracion", "/empresa/configuracion"],
  ])("%s va a Trámites con la misma ruta y parámetros", (path, expected) => {
    expect(redirect(path)).toBe(`${TRAMITES}${expected}`);
  });

  it("no confunde prefijos: /authors no es /auth/", () => {
    expect(redirect("/authors")).toBe(`${TRAMITES}/authors`);
  });
});
