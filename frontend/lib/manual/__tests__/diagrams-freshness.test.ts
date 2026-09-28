import { createHash } from "node:crypto";
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { MANUAL_ARTICLES } from "@/lib/manual/catalog";

/**
 * HU #13014 (Épica #12755) — guarda de frescura de los diagramas del manual.
 * AC3: editar un .mmd sin recompilar rompe la suite comparando el hash embebido en el SVG contra
 * la fuente. También exige que todo diagrama referenciado por un artículo tenga fuente .mmd, y
 * que no queden SVG huérfanos.
 */

const SRC_DIR = join(__dirname, "..", "diagrams");
const OUT_DIR = join(__dirname, "..", "..", "..", "public", "manual", "diagrams");

const sha256 = (text: string) => createHash("sha256").update(text, "utf8").digest("hex");

const fuentes = existsSync(SRC_DIR)
  ? readdirSync(SRC_DIR).filter((f) => f.endsWith(".mmd"))
  : [];

describe("manual/diagrams — frescura (AC3)", () => {
  it("todo .mmd tiene su SVG compilado con el hash de la fuente vigente", () => {
    const problemas: string[] = [];
    for (const file of fuentes) {
      const id = file.replace(/\.mmd$/, "");
      const svgPath = join(OUT_DIR, `${id}.svg`);
      if (!existsSync(svgPath)) {
        problemas.push(`${id}.svg no existe — corre pnpm manual:diagrams`);
        continue;
      }
      const svg = readFileSync(svgPath, "utf8");
      const hash = /<!-- mmd-sha256:([0-9a-f]{64}) -->/.exec(svg)?.[1];
      if (!hash) {
        problemas.push(`${id}.svg no trae el hash de su fuente — regenerar con el script`);
        continue;
      }
      const fuente = readFileSync(join(SRC_DIR, file), "utf8");
      if (hash !== sha256(fuente)) {
        problemas.push(`${id}.svg está desactualizado frente a ${file} — corre pnpm manual:diagrams`);
      }
    }
    expect(problemas).toEqual([]);
  });

  it("no hay SVG huérfanos (compilado sin fuente .mmd)", () => {
    const declarados = new Set(fuentes.map((f) => f.replace(/\.mmd$/, ".svg")));
    const compilados = existsSync(OUT_DIR)
      ? readdirSync(OUT_DIR).filter((f) => f.endsWith(".svg"))
      : [];
    expect(compilados.filter((f) => !declarados.has(f))).toEqual([]);
  });

  it("todo diagrama referenciado en un artículo tiene fuente .mmd", () => {
    const ids = new Set(fuentes.map((f) => f.replace(/\.mmd$/, "")));
    const sinFuente = MANUAL_ARTICLES.flatMap((a) =>
      a.blocks.flatMap((b) =>
        (b.media ?? [])
          .filter((m) => m.kind === "diagram" && !ids.has(m.id))
          .map((m) => `${a.slug}#${b.id} → ${(m as { id: string }).id}`),
      ),
    );
    expect(sinFuente).toEqual([]);
  });
});
