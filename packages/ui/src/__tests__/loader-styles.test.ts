import { createHash } from "node:crypto";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

// Uso de ejemplo: un frontend de la suite agrega `@import "@flit/ui/loader.css";` en su CSS global y
// `<VehicleLoader />` (o cualquier `.flit-escena-*`) se anima igual que en Trámites (HU #13729, RD-6).

const UI_ROOT = path.resolve(__dirname, "..", "..");
const REPO_ROOT = path.resolve(UI_ROOT, "..", "..");

const pkg = JSON.parse(readFileSync(path.join(UI_ROOT, "package.json"), "utf8")) as {
  exports: Record<string, string>;
};

/** Archivo al que apunta el export `@flit/ui/loader.css`, o `null` si el paquete no lo expone. */
function loaderCssPath(): string | null {
  const target = pkg.exports["./loader.css"];
  return target ? path.join(UI_ROOT, target) : null;
}

function readLoaderCss(): string {
  const file = loaderCssPath();
  if (!file || !existsSync(file)) throw new Error("@flit/ui no exporta ./loader.css");
  return readFileSync(file, "utf8");
}

/** CSS sin comentarios ni espacios irrelevantes: compara reglas, no formato. */
function normalizar(css: string): string {
  return css
    .replace(/\/\*[\s\S]*?\*\//g, "")
    .replace(/\s+/g, " ")
    .replace(/\s*([{}:;,>()])\s*/g, "$1")
    .trim();
}

/**
 * SHA-256 del bloque del loader tal como estaba en `frontend/app/globals.css` (líneas 75-142, dentro de
 * `@layer base`) ANTES de la mudanza, normalizado con `normalizar`. Si cambia un valor, cambia el hash.
 */
const HASH_LINEA_BASE = "f6f4872a50b27d5de2e9199aa47e2e332abcd239775b99ec928eedbb2e2b8024";

const CLASES = [
  ".flit-loader",
  ".flit-loader::before",
  ".flit-loader > .flit-vehicle",
  ".flit-escena-orbita",
  ".flit-escena-vehiculo",
];

describe("estilos del loader de @flit/ui", () => {
  it("AC1 — el paquete exporta ./loader.css y el archivo vive en src/styles/", () => {
    const file = loaderCssPath();
    expect(file).not.toBeNull();
    expect(path.relative(UI_ROOT, file as string).replace(/\\/g, "/")).toBe("src/styles/loader.css");
    expect(existsSync(file as string)).toBe(true);
  });

  it("AC1 — el export contiene .flit-loader, .flit-escena-* y sus keyframes", () => {
    const css = readLoaderCss();
    for (const selector of CLASES) expect(css).toContain(selector);
    expect(css).toContain("@keyframes flit-orbit");
    expect(css).toContain("@keyframes flit-counter");
    expect(css).toMatch(/@media \(prefers-reduced-motion: reduce\)/);
  });

  it("AC2 — las reglas movidas son idénticas a la línea base de Trámites (mismos valores)", () => {
    const hash = createHash("sha256").update(normalizar(readLoaderCss())).digest("hex");
    expect(hash).toBe(HASH_LINEA_BASE);
  });

  it("AC3 — frontend/app/globals.css ya no define .flit-loader ni .flit-escena-* y los importa de @flit/ui", () => {
    const globals = readFileSync(path.join(REPO_ROOT, "frontend", "app", "globals.css"), "utf8");
    expect(globals).not.toMatch(/\.flit-loader|\.flit-escena-|@keyframes flit-(orbit|counter)/);
    expect(globals).toContain('@import "@flit/ui/loader.css";');
  });

  it("AC3 — ningún CSS global de la suite duplica la definición", () => {
    for (const app of ["frontend", "frontend-hub", "frontend-comparendos"]) {
      const file = path.join(REPO_ROOT, app, "app", "globals.css");
      if (!existsSync(file)) continue;
      expect(readFileSync(file, "utf8"), app).not.toMatch(/\.flit-loader\s*[{,:>]|\.flit-escena-\w+\s*[{,]/);
    }
  });
});
