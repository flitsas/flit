import { describe, expect, it } from "vitest";
import { readdirSync, readFileSync } from "node:fs";
import { join, resolve } from "node:path";

// Uso de ejemplo: guarda de AC6. Recorre packages/ui/src/*.tsx y exige que cada var(--x) exista en tokens.css
// de @flit/ui y que ningún componente use tokens --dx-* (son locales de frontend-diagnostico).

const raizUi = resolve(__dirname, "../../../packages/ui/src");
const css = readdirSync(join(raizUi, "styles"))
  .filter((f) => f.endsWith(".css"))
  .map((f) => readFileSync(join(raizUi, "styles", f), "utf-8"))
  .join("\n");
const definidos = new Set([...css.matchAll(/(--[a-z0-9-]+)\s*:/gi)].map((m) => m[1]));
const componentes = readdirSync(raizUi).filter((f) => f.endsWith(".tsx"));

describe("AC6: @flit/ui solo usa tokens que ya existen", () => {
  it("encuentra componentes y tokens", () => {
    expect(componentes.length).toBeGreaterThan(10);
    expect(definidos.size).toBeGreaterThan(20);
  });

  it.each(componentes)("%s no usa --dx-* ni tokens sin definir", (archivo) => {
    const fuente = readFileSync(join(raizUi, archivo), "utf-8");
    const usados = [...fuente.matchAll(/var\((--[a-z0-9-]+)/gi)].map((m) => m[1])
      // var(--badge-${tono}): prefijo interpolado, no un token literal.
      .filter((t) => !t.endsWith("-"));
    expect(usados.filter((t) => t.startsWith("--dx-"))).toEqual([]);
    expect(usados.filter((t) => !definidos.has(t))).toEqual([]);
  });
});
