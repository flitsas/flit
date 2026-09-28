#!/usr/bin/env node
/**
 * HU #13014 (Épica #12755) — compila los diagramas Mermaid del manual a SVG estático.
 *
 * Fuentes:  lib/manual/diagrams/{id}.mmd
 * Salida:   public/manual/diagrams/{id}.svg  (con el hash de la fuente embebido para la guarda
 *           de frescura diagrams-freshness.test.ts)
 *
 * Uso:  pnpm manual:diagrams
 * No necesita la app corriendo: renderiza con el chromium de Playwright y el bundle local de
 * mermaid. Un .mmd inválido falla nombrando el archivo (AC2) sin publicar un SVG roto, y no
 * impide compilar los demás.
 */
import { createHash } from "node:crypto";
import { mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { chromium } from "playwright";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..");
const SRC_DIR = join(ROOT, "lib", "manual", "diagrams");
const OUT_DIR = join(ROOT, "public", "manual", "diagrams");
const MERMAID_BUNDLE = join(ROOT, "node_modules", "mermaid", "dist", "mermaid.min.js");

const sha256 = (text) => createHash("sha256").update(text, "utf8").digest("hex");

async function main() {
  const sources = readdirSync(SRC_DIR).filter((f) => f.endsWith(".mmd"));
  if (sources.length === 0) {
    console.log("manual:diagrams — no hay fuentes .mmd en lib/manual/diagrams/.");
    return;
  }
  mkdirSync(OUT_DIR, { recursive: true });

  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.goto(pathToFileURL(join(ROOT, "package.json")).href.replace("package.json", ""));
  await page.addScriptTag({ path: MERMAID_BUNDLE });
  await page.evaluate(() => {
    // Tema neutro sobre fondo blanco: la figura del manual fuerza backdrop claro, así que el
    // mismo SVG es legible en tema claro y oscuro de la app.
    window.mermaid.initialize({ startOnLoad: false, theme: "neutral", fontFamily: "Poppins, sans-serif" });
  });

  const failures = [];
  for (const file of sources) {
    const id = file.replace(/\.mmd$/, "");
    const source = readFileSync(join(SRC_DIR, file), "utf8");
    try {
      const svg = await page.evaluate(async ([renderId, definition]) => {
        const { svg } = await window.mermaid.render(renderId, definition);
        return svg;
      }, [`d-${id.replace(/[^a-z0-9]/g, "")}`, source]);
      const stamped = `<!-- mmd-sha256:${sha256(source)} -->\n${svg.trim()}\n`;
      writeFileSync(join(OUT_DIR, `${id}.svg`), stamped, "utf8");
      console.log(`  ✓ ${id}.svg`);
    } catch (error) {
      // AC2: nombrar el archivo inválido; no escribir salida rota; seguir con los demás.
      failures.push({ file, reason: (error.message ?? String(error)).split("\n")[0] });
      console.error(`  ✗ ${file}: ${(error.message ?? String(error)).split("\n")[0]}`);
    }
  }

  await browser.close();

  if (failures.length > 0) {
    console.error(
      `\nmanual:diagrams — ${failures.length} de ${sources.length} diagramas fallaron:\n` +
        failures.map((f) => `  - ${f.file}: ${f.reason}`).join("\n"),
    );
    process.exit(1);
  }
  console.log(`\nmanual:diagrams — ${sources.length} diagramas compilados en public/manual/diagrams/.`);
}

main().catch((error) => {
  console.error(`manual:diagrams — ${error.message}`);
  process.exit(1);
});
