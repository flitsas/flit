import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { ASSETS } from "@/lib/assets";

const publico = path.resolve(__dirname, "../public");
const rutas = [ASSETS.logo, ...Object.values(ASSETS.acciones), ...Object.values(ASSETS.pasos)];

describe("imágenes propias del diseño", () => {
  it.each(rutas)("%s existe y es un SVG", (ruta) => {
    const archivo = path.join(publico, ruta);

    expect(existsSync(archivo)).toBe(true);
    expect(readFileSync(archivo, "utf8")).toContain("<svg");
  });

  it.each(ASSETS.banners.map((b) => [b.src, b]))("banner %s existe, es JPEG y tiene texto alternativo", (_src, banner) => {
    const bytes = readFileSync(path.join(publico, banner.src));

    expect([bytes[0], bytes[1]]).toEqual([0xff, 0xd8]);
    expect(banner.alt.length).toBeGreaterThan(20);
  });

  it("los SVG no traen scripts ni referencias externas", () => {
    for (const ruta of rutas) {
      const svg = readFileSync(path.join(publico, ruta), "utf8");

      expect(svg).not.toMatch(/<script|\son[a-z]+=|href=["']https?:/i);
    }
  });
});
