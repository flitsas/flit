import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, relative, resolve } from "node:path";
import { afterAll, describe, expect, it } from "vitest";

// Uso de ejemplo: buscarInfracciones(REPO, ["packages", "frontend"]) → ["packages/ui/src/x.css"] si algún archivo fuera
// de frontend-diagnostico declara un token «--dx-», nombra un «IconoDx» o importa «@/components/dx/».
//
// HU #14562 · UI-3 (D2 = c): el UI Kit 2026 aplica solo a Diagnóstico. Sus tokens y sus íconos propios viven en
// frontend-diagnostico y no pueden filtrarse a @flit/ui, @flit/shell ni a otro producto de la suite. `scripts/ci/` no
// existe, así que la guarda es esta prueba de vitest, que el CI ya corre.

const DIAGNOSTICO = resolve(__dirname, "../..");
const REPO = resolve(DIAGNOSTICO, "..");
const RAICES = ["packages", "frontend", "frontend-hub", "frontend-comparendos", "frontend-personanatural"];
const IGNORAR = new Set(["node_modules", ".next", "dist", "build", "out", "coverage", "test-results", "playwright-report", ".turbo"]);
const EXTENSIONES = /\.(ts|tsx|js|jsx|mjs|cjs|css|scss)$/;
const PATRONES = [/--dx-/, /IconoDx/, /@\/components\/dx\//];

function archivos(dir: string): string[] {
  let nombres: string[];
  try {
    nombres = readdirSync(dir);
  } catch {
    return [];
  }
  return nombres.flatMap((nombre) => {
    if (IGNORAR.has(nombre)) return [];
    const ruta = join(dir, nombre);
    if (statSync(ruta).isDirectory()) return archivos(ruta);
    return EXTENSIONES.test(nombre) ? [ruta] : [];
  });
}

function buscarInfracciones(base: string, raices: string[]): string[] {
  return raices
    .flatMap((raiz) => archivos(join(base, raiz)))
    .filter((ruta) => {
      const codigo = readFileSync(ruta, "utf8");
      return PATRONES.some((p) => p.test(codigo));
    })
    .map((ruta) => relative(base, ruta).split("\\").join("/"));
}

const TOKENS = readFileSync(join(DIAGNOSTICO, "app", "dx", "tokens-dx.css"), "utf8");
const GLOBALS = readFileSync(join(DIAGNOSTICO, "app", "globals.css"), "utf8");

function declarados(bloque: string): string[] {
  return [...bloque.matchAll(/(--dx-[a-z0-9-]+)\s*:/g)].map((m) => m[1]);
}

function bloque(re: RegExp): string {
  const m = TOKENS.match(re);
  expect(m, `no encontré el bloque ${re}`).not.toBeNull();
  return (m as RegExpMatchArray)[1];
}

const ESTADOS = ["pendiente", "en-curso", "ejecutado", "deuda", "error", "novedad", "sin-solicitar"];
// HU #14562 · m-1: sin -fondo ni --dx-panel-lateral-ancho (no tenían consumidor; ver el bloque m-1 al final).
const OBLIGATORIOS = [
  ...ESTADOS.flatMap((e) => [`--dx-estado-${e}`, `--dx-estado-${e}-borde`]),
  "--dx-radio",
  "--dx-sombra-dock",
  "--dx-motion-rapido",
  "--dx-motion-base",
];

describe("AC1 · tokens --dx-* aislados en Diagnóstico", () => {
  it("ningún archivo fuera de frontend-diagnostico usa --dx-, IconoDx ni @/components/dx/", () => {
    const infractores = buscarInfracciones(REPO, RAICES);
    expect(infractores, `Archivos fuera de Diagnóstico con tokens o íconos propios: ${infractores.join(", ")}`).toEqual([]);
  });

  it("globals.css importa tokens-dx.css después de @flit/ui/tokens.css y antes de @flit/shell/shell.css", () => {
    const ui = GLOBALS.indexOf('@import "@flit/ui/tokens.css"');
    const dx = GLOBALS.indexOf('@import "./dx/tokens-dx.css"');
    const shell = GLOBALS.indexOf('@import "@flit/shell/shell.css"');
    expect(ui).toBeGreaterThanOrEqual(0);
    expect(dx).toBeGreaterThan(ui);
    expect(shell).toBeGreaterThan(dx);
  });

  it("declara en :root los tokens del inventario: estados (con borde), radio, sombra y motion", () => {
    const raiz = declarados(bloque(/(?:^|\n):root\s*\{([^}]*)\}/));
    for (const token of OBLIGATORIOS) expect(raiz, token).toContain(token);
  });

  it("cada token --dx-* de :root tiene su variante .dark", () => {
    const raiz = declarados(bloque(/(?:^|\n):root\s*\{([^}]*)\}/));
    const oscuro = declarados(bloque(/(?:^|\n)\.dark\s*\{([^}]*)\}/));
    expect(raiz.length).toBeGreaterThan(0);
    for (const token of raiz) expect(oscuro, `${token} sin variante .dark`).toContain(token);
  });

  it("cada token tiene una línea de inventario que explica por qué no sirve el de @flit/ui", () => {
    const cabecera = TOKENS.slice(0, TOKENS.indexOf("*/"));
    const raiz = declarados(bloque(/(?:^|\n):root\s*\{([^}]*)\}/));
    // Una línea por token base: los sufijos -fondo y -borde comparten la línea de su estado.
    const bases = [...new Set(raiz.map((t) => t.replace(/-(fondo|borde)$/, "")))];
    for (const token of bases) {
      const linea = cabecera.split("\n").find((l) => l.includes(`${token} `) || l.includes(`${token}:`));
      expect(linea, `${token} sin línea de inventario`).toBeDefined();
      expect(linea as string, `${token}: la línea debe explicar el motivo («—»)`).toMatch(/—\s*\S/);
    }
  });

  it("expone los estados como colores de Tailwind (--color-dx-estado-*) en @theme inline", () => {
    const tema = bloque(/@theme inline\s*\{([^}]*)\}/);
    for (const e of ESTADOS) expect(tema).toContain(`--color-dx-estado-${e}: var(--dx-estado-${e})`);
  });

  it("solo declara tokens: sin !important ni reglas de componente (el aspecto entra por className)", () => {
    expect(TOKENS).not.toMatch(/!important/);
    expect(TOKENS).not.toMatch(/\n\.(?!dark\b)[a-z][\w-]*\s*\{/i);
  });
});

describe("AC5 · motion y movimiento reducido", () => {
  it("--dx-motion-rapido es 120ms y --dx-motion-base 180ms", () => {
    const raiz = bloque(/(?:^|\n):root\s*\{([^}]*)\}/);
    expect(raiz).toMatch(/--dx-motion-rapido:\s*120ms/);
    expect(raiz).toMatch(/--dx-motion-base:\s*180ms/);
  });

  it("con prefers-reduced-motion: reduce todos los --dx-motion-* valen 0ms", () => {
    const reducido = bloque(/@media \(prefers-reduced-motion: reduce\)\s*\{([\s\S]*?)\n\}/);
    const motion = declarados(bloque(/(?:^|\n):root\s*\{([^}]*)\}/)).filter((t) => t.startsWith("--dx-motion-"));
    expect(motion.length).toBeGreaterThanOrEqual(2);
    for (const token of motion) expect(reducido).toMatch(new RegExp(`${token}:\\s*0ms`));
  });

});

// HU #14562 · m-1 (auditoría de UI): un token del inventario que nadie consume sobra. Se retiraron los -fondo (el chip
// del kit calcula su fondo al 10 % del borde), --dx-panel-lateral-ancho (PanelLateral ya mide 640 px por defecto) y
// --dx-icono-tinte (el visto del dictamen se traza con la superficie, m-10).
describe("m-1 · todo token --dx-* tiene consumidor", () => {
  it("cada token declarado en :root se usa fuera de tokens-dx.css y de las pruebas", () => {
    const fuentes = ["components", "app", "lib"]
      .flatMap((raiz) => archivos(join(DIAGNOSTICO, raiz)))
      .filter((ruta) => !/tokens-dx\.css$/.test(ruta) && !/(__tests__|\.test\.)/.test(ruta))
      .map((ruta) => readFileSync(ruta, "utf8"))
      .join("\n");
    // El semáforo arma el nombre con el tono: `var(--dx-estado-${tono})` y `var(--dx-estado-${tono}-borde)`.
    const porTono = (token: string) => {
      const m = token.match(/^--dx-estado-(.+?)(-borde)?$/);
      return !!m && ESTADOS.includes(m[1]) && fuentes.includes("var(--dx-estado-${tono}" + (m[2] ?? "") + ")");
    };
    const raiz = declarados(bloque(/(?:^|\n):root\s*\{([^}]*)\}/));
    const sinUso = raiz.filter((t) => !fuentes.includes(`var(${t})`) && !porTono(t));
    expect(sinUso, `tokens sin consumidor: ${sinUso.join(", ")}`).toEqual([]);
  });
});

describe("AC2 · aislamiento violado (negativo)", () => {
  const base = mkdtempSync(join(tmpdir(), "dx-aislamiento-"));
  afterAll(() => rmSync(base, { recursive: true, force: true }));

  it("la guarda señala cada archivo infractor y omite node_modules", () => {
    mkdirSync(join(base, "packages", "ui", "src"), { recursive: true });
    mkdirSync(join(base, "frontend-hub", "components"), { recursive: true });
    mkdirSync(join(base, "frontend", "node_modules", "x"), { recursive: true });
    writeFileSync(join(base, "packages", "ui", "src", "estilo.css"), ":root { --dx-estado-error: #c10000; }");
    writeFileSync(
      join(base, "frontend-hub", "components", "Tarjeta.tsx"),
      'import { IconoDxRunt } from "@/components/dx/iconos/IconoDxRunt";',
    );
    writeFileSync(join(base, "frontend", "node_modules", "x", "y.css"), ":root { --dx-radio: 9px; }");
    writeFileSync(join(base, "frontend-hub", "components", "Limpio.tsx"), "export const x = 'var(--kit-azul)';");

    expect(buscarInfracciones(base, RAICES).sort()).toEqual(
      ["frontend-hub/components/Tarjeta.tsx", "packages/ui/src/estilo.css"].sort(),
    );
  });
});
