// HU #12415 — regla de lint acotada a las superficies del alcance cerrado de
// Marca Blanca (#12237): acceso, activación, recuperación, cabecera/menú e
// icono/título de la pestaña. Se aplica SOLO a los globs declarados en
// eslint.config.mjs (MARCA_BLANCA_SURFACE_GLOBS) — nunca al resto del repo
// (AC3: "sin afectar al resto del repositorio").
//
// No es zero-tolerance desde el día uno: hoy esas mismas superficies todavía
// tienen los 6 hex de marca escritos a mano (el saneamiento es HU #12420, y
// AC5 de #12415 prohíbe tocar código de producto en esta historia). Por eso la
// regla solo reporta apariciones que NO estén en el inventario congelado
// (frontend/docs/brand-color-inventory.json, ruta:línea:hex) — es decir,
// colores de marca NUEVOS que aparezcan después de esta HU. Si una línea ya
// existente cambia de hex o se mueve de línea, deja de matchear el inventario
// y por tanto también se reporta (fuerza a regenerar el inventario con
// `pnpm brand:inventory` en el mismo PR que la toque).
//
// Uso de ejemplo (flat config):
//   import noLiteralBrandColor from "./eslint-rules/no-literal-brand-color.mjs";
//   { files: MARCA_BLANCA_SURFACE_GLOBS,
//     plugins: { "brand-color": { rules: { "no-literal-brand-color": noLiteralBrandColor } } },
//     rules: { "brand-color/no-literal-brand-color": "error" } }

import { readFileSync, existsSync } from "node:fs";
import path from "node:path";

const BRAND_HEX_PATTERN = /#([0-9a-fA-F]{6})\b/g;
const BRAND_HEXES = new Set(["162744", "557eff", "00dbd5", "ff4e00", "eef5ff", "dfe5ed"]);

function loadBaseline() {
  // Resuelto en tiempo de carga del módulo (una vez por proceso de ESLint).
  // No usa import.meta aquí para mantener el módulo simple de testear en
  // aislamiento vía `createRule(baseline)`.
  const candidate = path.resolve(process.cwd(), "docs/brand-color-inventory.json");
  if (!existsSync(candidate)) return new Set();
  try {
    const data = JSON.parse(readFileSync(candidate, "utf8"));
    const keys = new Set();
    for (const surface of data.surfaces ?? []) {
      for (const file of surface.files ?? []) {
        for (const occ of file.occurrences ?? []) {
          if (occ.classification === "marca") {
            keys.add(`${file.relPath}:${occ.line}:${occ.hexNormalized}`);
          }
        }
      }
    }
    return keys;
  } catch {
    return new Set();
  }
}

let baselineCache = null;
function getBaseline() {
  if (baselineCache === null) baselineCache = loadBaseline();
  return baselineCache;
}

/** Fábrica testeable: permite inyectar un baseline sin tocar el filesystem. */
export function createRule(baseline) {
  return {
    meta: {
      type: "problem",
      docs: {
        description:
          "Prohíbe colores de marca FLIT escritos a mano (hex literal) fuera del inventario congelado, en las superficies del alcance cerrado de Marca Blanca (HU #12415). Usa var(--brand-*)/var(--color-flit-*) (ADR-0060 §D5, HU #12419/#12420).",
      },
      schema: [],
      messages: {
        literalBrandColor:
          "Color de marca escrito a mano ('{{hex}}') no registrado en el inventario (frontend/docs/brand-color-inventory.json). Usa var(--brand-*)/var(--color-flit-*), o si es un cambio legítimo dentro del alcance de saneamiento, regenera el inventario con `pnpm brand:inventory`.",
      },
    },
    create(context) {
      const filename = context.filename ?? context.getFilename();
      const cwd = context.cwd ?? process.cwd();
      const relPath = path.relative(cwd, filename).split(path.sep).join("/");

      function check(node, raw, line) {
        if (typeof raw !== "string") return;
        const regex = new RegExp(BRAND_HEX_PATTERN.source, "g");
        let match;
        while ((match = regex.exec(raw)) !== null) {
          const hex = match[1].toLowerCase();
          if (!BRAND_HEXES.has(hex)) continue;
          const key = `${relPath}:${line}:#${hex}`;
          if (baseline.has(key)) continue;
          context.report({ node, messageId: "literalBrandColor", data: { hex: `#${hex}` } });
        }
      }

      return {
        Literal(node) {
          check(node, node.value, node.loc.start.line);
        },
        TemplateElement(node) {
          check(node, node.value.raw, node.loc.start.line);
        },
      };
    },
  };
}

/** @type {import("eslint").Rule.RuleModule} */
const rule = createRule(getBaseline());

export default rule;
