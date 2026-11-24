import { describe, expect, it } from "vitest";
import { hrefPestana, pestanasVisibles } from "@/components/reportes/PestanasReportes";
import type { SesionComparendos } from "@/lib/sesion";

function sesion(permisos: string[], esSuperAdmin = false): SesionComparendos {
  return { usuario: { id: "u", email: "u@x.co", esSuperAdmin }, permisos } as unknown as SesionComparendos;
}

const LECTURA = ["comparendos.reportes.ver", "comparendos.productividad.ver", "comparendos.reportes.consultas"];

describe("Pestañas de Reportes", () => {
  it("un rol de lectura ve todo menos Plataforma, con Productividad como pestaña", () => {
    expect(pestanasVisibles(sesion(LECTURA)).map((p) => p.id)).toEqual(["resumen", "gestion", "dp", "productividad", "consultas"]);
  });

  it("Plataforma es solo del SuperAdmin", () => {
    expect(pestanasVisibles(sesion([], true)).map((p) => p.id)).toContain("plataforma");
    expect(pestanasVisibles(sesion(LECTURA)).map((p) => p.id)).not.toContain("plataforma");
  });

  it("sin el permiso de la pestaña, no aparece", () => {
    expect(pestanasVisibles(sesion(["comparendos.reportes.ver"])).map((p) => p.id)).toEqual(["resumen", "gestion", "dp"]);
  });

  it("al cambiar de pestaña se conservan periodo, NIT y empresa, y nada más", () => {
    const actuales = new URLSearchParams("desde=2026-01-01&hasta=2026-01-31&nit=900&empresa=e1&q=abc&tab=x");
    expect(hrefPestana("gestion", actuales)).toBe("/reportes/gestion?desde=2026-01-01&hasta=2026-01-31&nit=900&empresa=e1");
    expect(hrefPestana(null, new URLSearchParams())).toBe("/reportes");
  });
});
