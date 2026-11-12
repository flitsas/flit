import { describe, expect, it } from "vitest";
import { BRAND_COLOR_PRESETS } from "@/components/admin/branding/brand-color-presets";
import { pickOnPrimary } from "@/lib/brand/derive-tokens";
import { contrastRatio } from "@/lib/brand/contrast";
import { ajustarColoresPortal, coloresPortalLegibles, FONDO_PORTAL, titulosLegibles } from "@/lib/brand/portal-contrast";

describe("reglas de lectura de la marca del portal (Épica #14297)", () => {
  it("un principal claro no pasa aunque el texto oscuro sobre él se lea: el nombre y los títulos no se verían", () => {
    const colores = { primary: "#FFE066", secondary: "#5D96C1", onPrimary: "#162744" };
    expect(titulosLegibles(colores)).toBe(false);
    expect(coloresPortalLegibles(colores)).toBe(false);
  });

  it("Ajustar deja una combinación que cumple todos los pares, sin cambiar los colores que ya servían", () => {
    const ajustados = ajustarColoresPortal({ primary: "#FFE066", secondary: "#0A4A70" });
    expect(coloresPortalLegibles(ajustados)).toBe(true);
    expect(ajustados.onPrimary).toBe("#FFFFFF");
    expect(ajustados.secondary).toBe("#0A4A70");
    expect(contrastRatio(ajustados.primary, FONDO_PORTAL)).toBeGreaterThanOrEqual(3);
  });

  it.each(["#FFFFFF", "#FFE066", "#00DBD5", "#8CC63F", "#000000"])("Ajustar termina con colores legibles desde %s", (color) => {
    expect(coloresPortalLegibles(ajustarColoresPortal({ primary: color, secondary: color }))).toBe(true);
  });

  it("todas las combinaciones sugeridas se pueden publicar", () => {
    for (const p of BRAND_COLOR_PRESETS) {
      expect(coloresPortalLegibles({ primary: p.primary, secondary: p.secondary, onPrimary: pickOnPrimary(p.primary, p.secondary) })).toBe(true);
    }
  });
});
