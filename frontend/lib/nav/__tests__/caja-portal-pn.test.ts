// La Caja vive en Portal Persona Natural: la persona de caja (solo ot_caja) abre la ruta; el resto de /admin sigue cerrado.
import { describe, expect, it } from "vitest";
import { cajaPortalPath, SEGMENTO_ORGANISMO_PROPIO } from "@/components/admin/transit-offices/ot-nav";
import type { JwtPayload } from "@/lib/auth/jwt";
import { resolveRouteAccess } from "../module-access";

const cajero: JwtPayload = { entity_type: "TRANSIT_OFFICE", roles: [{ id: "x", code: "ot_caja" }] };

describe("ruta de la persona de caja", () => {
  it("Portal Persona Natural abre para ot_caja", () => {
    expect(resolveRouteAccess(cajero, "/admin/transit-offices/t-1/portal-persona-natural").allowed).toBe(true);
  });

  it("el resto de las pestañas del organismo y /admin siguen cerrados para ella", () => {
    for (const ruta of [
      "/admin/transit-offices/t-1/client-procedures",
      "/admin/transit-offices/t-1/usuarios",
      "/admin/transit-offices/t-1/reportes",
      "/admin/transit-offices/t-1/configuracion",
      "/admin/companies",
      "/admin/banners",
    ]) {
      expect(resolveRouteAccess(cajero, ruta).allowed, ruta).toBe(false);
    }
  });

  it("cajaPortalPath arma la URL con su organismo (tenant del token) y la pestaña Caja", () => {
    expect(cajaPortalPath("t-1")).toBe("/admin/transit-offices/t-1/portal-persona-natural?tab=caja");
    expect(cajaPortalPath("t-1", "cobros")).toBe("/admin/transit-offices/t-1/portal-persona-natural?tab=caja&sub=cobros");
    expect(cajaPortalPath(null)).toBe(`/admin/transit-offices/${SEGMENTO_ORGANISMO_PROPIO}/portal-persona-natural?tab=caja`);
  });
});
