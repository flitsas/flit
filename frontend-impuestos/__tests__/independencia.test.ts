// @vitest-environment node
import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

// Uso de ejemplo: si un archivo de frontend-impuestos hace `import … from "../frontend-diagnostico/…"` o el package.json
// declara @flit/frontend-diagnostico, esta prueba falla (EP-CF1 / AC-FE-43: los productos solo comparten @flit/*).

const RAIZ = path.resolve(__dirname, "..");
const IGNORAR = new Set(["node_modules", ".next", "out", "build", "coverage", "public"]);
const ESTE_ARCHIVO = path.resolve(__filename);

function archivos(dir: string): string[] {
  return readdirSync(dir).flatMap((nombre) => {
    if (IGNORAR.has(nombre)) return [];
    const ruta = path.join(dir, nombre);
    if (statSync(ruta).isDirectory()) return archivos(ruta);
    return /\.(ts|tsx|mts|mjs|js|cjs|json|css)$/.test(nombre) ? [ruta] : [];
  });
}

const fuentes = archivos(RAIZ).filter((r) => r !== ESTE_ARCHIVO);
const relativa = (r: string) => path.relative(RAIZ, r).replace(/\\/g, "/");

/** Especificadores de import/require/export…from y de `@import`/`@source` de CSS. */
function especificadores(texto: string): string[] {
  const patrones = [
    /\bfrom\s+["']([^"']+)["']/g,
    /\bimport\s*\(\s*["']([^"']+)["']\s*\)/g,
    /\bimport\s+["']([^"']+)["']/g,
    /\brequire\s*\(\s*["']([^"']+)["']\s*\)/g,
    /\bvi\.mock\s*\(\s*["']([^"']+)["']/g,
    /@(?:import|source)\s+["']([^"']+)["']/g,
  ];
  return patrones.flatMap((p) => Array.from(texto.matchAll(p), (m) => m[1]));
}

describe("independencia de frontend-impuestos (EP-CF1)", () => {
  it("encuentra el código de la app (la prueba no pasa en vacío)", () => {
    const rel = fuentes.map(relativa);
    expect(rel).toEqual(expect.arrayContaining(["middleware.ts", "app/layout.tsx", "components/ImpuestosShell.tsx", "package.json"]));
  });

  it("ningún archivo importa nada de frontend-diagnostico", () => {
    const culpables = fuentes.flatMap((ruta) =>
      especificadores(readFileSync(ruta, "utf8"))
        .filter((s) => /frontend-diagnostico/.test(s) || path.resolve(path.dirname(ruta), s).replace(/\\/g, "/").includes("/frontend-diagnostico"))
        .map((s) => `${relativa(ruta)} → ${s}`),
    );
    expect(culpables).toEqual([]);
  });

  it("package.json solo depende de paquetes compartidos @flit/* (nunca de otro producto)", () => {
    const pkg = JSON.parse(readFileSync(path.join(RAIZ, "package.json"), "utf8")) as Record<string, Record<string, string> | string>;
    expect(pkg.name).toBe("@flit/frontend-impuestos");
    const deps = { ...(pkg.dependencies as Record<string, string>), ...(pkg.devDependencies as Record<string, string>) };
    const deLaSuite = Object.keys(deps).filter((d) => d.startsWith("@flit/"));
    expect(deLaSuite.filter((d) => d.startsWith("@flit/frontend-"))).toEqual([]);
    expect(deLaSuite.sort()).toEqual(["@flit/auth", "@flit/brand", "@flit/shell", "@flit/ui"]);
  });

  it("los alias de tsconfig y vitest no apuntan fuera de la app ni a otro producto", () => {
    for (const archivo of ["tsconfig.json", "vitest.config.ts", "next.config.ts"]) {
      expect(readFileSync(path.join(RAIZ, archivo), "utf8"), archivo).not.toMatch(/frontend-(diagnostico|comparendos|personanatural|hub)\b|["'][^"']*\.\.\/frontend[/"']/);
    }
  });

  it("D-UI-GRAFICAS: las gráficas del dashboard son copias locales en components/estadisticas (no de Diagnóstico ni de packages/ui)", () => {
    const rel = fuentes.map(relativa);
    const graficas = ["Figura.tsx", "BarrasSerie.tsx", "ListaBarras.tsx", "TablaAlternativa.tsx"].map((f) => `components/estadisticas/${f}`);
    expect(rel).toEqual(expect.arrayContaining([...graficas, "app/(app)/page.tsx", "app/(app)/reportes/page.tsx"]));
    for (const g of graficas) expect(especificadores(readFileSync(path.join(RAIZ, g), "utf8")).filter((e) => /graficas|frontend-diagnostico/.test(e)), g).toEqual([]);
  });

  it("AC-FE-43: el código de la app no escribe en el almacenamiento del navegador (ni tokens ni credenciales)", () => {
    const usos = fuentes
      .filter((r) => !relativa(r).startsWith("__tests__/") && !/\.test\.tsx?$/.test(r) && !relativa(r).startsWith("vitest."))
      .filter((r) => /\b(localStorage|sessionStorage|indexedDB)\b|document\.cookie\s*=/.test(readFileSync(r, "utf8")))
      .map(relativa);
    expect(usos).toEqual([]);
  });
});
