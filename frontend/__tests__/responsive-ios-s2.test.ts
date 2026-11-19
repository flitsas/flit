import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

/**
 * Responsive iPhone/iPad (S2, contrato `.trees/responsive-CONTRATO.md`) — guardas estáticas de las trampas de WebKit
 * que la emulación de Playwright no ve: en el simulador `100vh` mide lo mismo que el área visible y el zoom al enfocar
 * un campo no ocurre. Si una falla, el mensaje dice el archivo.
 */

const FE_ROOT = path.resolve(__dirname, "..");

function fuentes(dir: string): string[] {
  return readdirSync(dir).flatMap((nombre) => {
    const ruta = path.join(dir, nombre);
    if (statSync(ruta).isDirectory()) return nombre === "__tests__" ? [] : fuentes(ruta);
    return /\.(tsx?|css)$/.test(ruta) ? [ruta] : [];
  });
}

const sinComentarios = (src: string) =>
  src.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:"'`])\/\/[^\n]*/g, "$1");

const ARCHIVOS = ["app", "components", "styles"].flatMap((c) => fuentes(path.join(FE_ROOT, c))).map((ruta) => ({
  rel: path.relative(FE_ROOT, ruta).split(path.sep).join("/"),
  src: sinComentarios(readFileSync(ruta, "utf8")),
}));

describe("responsive iOS — S2 Trámites/admin", () => {
  it("ningún alto de pantalla en vh: en iOS la barra de Safari lo tapa (va dvh, o nada dentro del shell)", () => {
    const malos = ARCHIVOS.filter(({ src }) => /\b(min-h-screen|h-screen)\b|\d+vh\b/.test(src)).map((a) => a.rel);
    expect(malos).toEqual([]);
  });

  it("sin cortes fuera de la escala de Tailwind (max-[640px], 900/901…)", () => {
    const malos = ARCHIVOS.filter(({ src }) =>
      /(max|min)-\[(480|640|900|901|961)px\]|(max|min)-width:\s*(540|541|640|641|760|761|900|901|960|961|1100|1200)px/.test(
        src,
      ),
    ).map((a) => a.rel);
    expect(malos).toEqual([]);
  });

  it("el layout declara el viewport con viewport-fit=cover y sin bloquear el zoom", () => {
    const layout = sinComentarios(readFileSync(path.join(FE_ROOT, "app/layout.tsx"), "utf8"));
    expect(layout).toMatch(/export const viewport: Viewport = \{[^}]*viewportFit: "cover"/);
    expect(layout).not.toMatch(/maximumScale|userScalable/);
  });

  it("por debajo de 640px ningún campo de texto queda bajo 16px (iOS hace zoom al enfocarlo)", () => {
    const css = sinComentarios(readFileSync(path.join(FE_ROOT, "app/globals.css"), "utf8"));
    const bloque = css.match(/@media \(max-width: 639\.98px\) \{([\s\S]*?)\n\}/)?.[1] ?? "";
    expect(bloque).toMatch(/select,\s*textarea/);
    expect(bloque).toMatch(/font-size: 16px/);
  });

  it("FiltroColumna no lee window durante el render (el ancho del panel va en CSS)", () => {
    const src = sinComentarios(readFileSync(path.join(FE_ROOT, "components/atom/FiltroColumna.tsx"), "utf8"));
    expect(src).not.toMatch(/typeof window === "undefined" \? PANEL_ANCHO/);
    expect(src).toMatch(/const ancho = `min\(\$\{PANEL_ANCHO\}px, calc\(100vw - 16px\)\)`/);
  });

  it("las acciones de una consulta guardada se ven en táctil (sin hover solo se ocultan donde hay puntero)", () => {
    const src = readFileSync(path.join(FE_ROOT, "components/consultas/SavedQueryList.tsx"), "utf8");
    expect(src).not.toMatch(/gap-0\.5 opacity-0 /);
    expect(src).toMatch(/opacity-100 \[@media\(hover:hover\)\]:opacity-0/);
  });
});
