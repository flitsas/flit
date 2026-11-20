import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

// Contrato responsive iPhone/iPad (.trees/responsive-CONTRATO.md §1, sesión S4). jsdom no evalúa media queries ni
// `env()`, así que se protege el CSS y el layout como texto: lo visual se revisa con el humo WebKit y a ojo.
const css = readFileSync(path.resolve(__dirname, "../app/globals.css"), "utf8");
const layout = readFileSync(path.resolve(__dirname, "../app/layout.tsx"), "utf8");

/** Reglas de un selector exacto (`.x { … }`), estén o no dentro de una media query. */
function reglas(selector: string): string[] {
  const escapado = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return [...css.matchAll(new RegExp(`${escapado}\\s*\\{([^}]*)\\}`, "g"))].map((m) => m[1]);
}

describe("responsive iOS de persona natural", () => {
  it("los cortes de ancho están en la escala 640/768/1024/1280, con el .98 en los máximos", () => {
    const anchos = [...css.matchAll(/\((max|min)-width:\s*([\d.]+)px\)/g)].map((m) => `${m[1]}-${m[2]}`);
    const permitidos = new Set(["max-639.98", "max-767.98", "max-1023.98", "max-1279.98", "min-640", "min-768", "min-1024", "min-1280"]);
    expect(anchos.filter((a) => !permitidos.has(a))).toEqual([]);
  });

  it("el pie fijo y los lanzadores (chat y Dr. Flit) suman la zona segura de abajo", () => {
    for (const selector of [".dashboard-footer", ".chat-launcher", ".dr-flit-tab"]) {
      const pegadosAbajo = reglas(selector).filter((r) => /(^|;)\s*(bottom|padding-bottom)\s*:/.test(r));
      expect(pegadosAbajo.length, selector).toBeGreaterThan(0);
      for (const r of pegadosAbajo) expect(r, selector).toContain("env(safe-area-inset-bottom)");
    }
    for (const r of reglas(".chat-widget").filter((x) => x.includes("--chat-bottom:"))) expect(r).toContain("env(safe-area-inset-bottom)");
  });

  it("todo backdrop-filter lleva su -webkit-", () => {
    const sinPrefijo = css.match(/(?<!-webkit-)backdrop-filter\s*:/g) ?? [];
    const conPrefijo = css.match(/-webkit-backdrop-filter\s*:/g) ?? [];
    expect(conPrefijo.length).toBe(sinPrefijo.length);
  });

  it("los campos achicados vuelven a 16 px por debajo de 640 (iOS no hace zoom)", () => {
    const movil = css.slice(css.lastIndexOf("@media (max-width: 639.98px)"));
    for (const selector of [".dashboard-label .field", ".dashboard-field-row .field", ".dashboard-consultation-form .field", ".action-blue .dashboard-registration-fields .field"]) {
      expect(movil.split("}")[0], selector).toContain(selector);
    }
    expect(movil.split("}")[0]).toContain("font-size: 16px");
  });

  it("la tabla de ficha se vuelve tarjeta por debajo de 1024 sin duplicar DOM", () => {
    expect(css).toMatch(/@media \(max-width: 1023\.98px\) \{\s*\.tabla-ficha \{ display: grid;/);
    expect(css).toContain(".tabla-ficha > thead, .tabla-ficha > tbody, .tabla-ficha tr { display: contents; }");
  });

  it("trae la capa base de F0 y el viewport con viewport-fit=cover, sin bloquear el zoom", () => {
    expect(css).toContain("-webkit-text-size-adjust: 100%");
    expect(css).toMatch(/html,\s*body\s*\{\s*overflow-x: clip;/);
    const viewport = layout.match(/export const viewport: Viewport = \{[^}]*\}/)?.[0] ?? "";
    expect(viewport).toContain('viewportFit: "cover"');
    expect(viewport).not.toMatch(/maximumScale|userScalable/);
  });
});
