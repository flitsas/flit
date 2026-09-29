import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";

import { describe, expect, it } from "vitest";

import { MANUAL_ARTICLES } from "@/lib/manual/catalog";
import {
  DR_FLIT_MANUAL_CATALOG_PATHS,
  DR_FLIT_MANUAL_CATALOG_SCHEMA_VERSION,
  buildDrFlitManualCatalog,
  serializeDrFlitManualCatalog,
} from "@/lib/manual/dr-flit-catalog-export";
import type { ManualArticle } from "@/lib/manual/types";

/**
 * HU #12920 — guarda de desfase del artefacto del manual que consume el backend de DR. FLIT.
 *
 * Uso de ejemplo:
 *   pnpm manual:export   → regenera los dos artefactos (corre este archivo con DR_FLIT_MANUAL_EXPORT=write)
 *   pnpm test            → falla si alguien editó lib/manual/ y no regeneró
 */
const REPO_ROOT = path.resolve(__dirname, "../../../..");
const WRITE = process.env.DR_FLIT_MANUAL_EXPORT === "write";
const REGENERATE_HINT = "El manual cambió y el artefacto de DR. FLIT no. Corre `pnpm manual:export` y commitea el resultado.";

describe("artefacto del manual para DR. FLIT", () => {
  const expected = serializeDrFlitManualCatalog();

  if (WRITE) {
    it.each(DR_FLIT_MANUAL_CATALOG_PATHS)("escribe %s", (relative) => {
      const target = path.join(REPO_ROOT, relative);
      mkdirSync(path.dirname(target), { recursive: true });
      writeFileSync(target, expected, "utf8");
      expect(readFileSync(target, "utf8")).toBe(expected);
    });
    return;
  }

  // AC2 — si alguien edita un artículo sin regenerar, CI falla aquí.
  it.each(DR_FLIT_MANUAL_CATALOG_PATHS)("%s está al día con lib/manual", (relative) => {
    const committed = readFileSync(path.join(REPO_ROOT, relative), "utf8").replace(/\r\n/g, "\n");
    expect(committed, REGENERATE_HINT).toBe(expected);
  });
});

describe("buildDrFlitManualCatalog", () => {
  // AC1 — cada artículo conserva slug, título, audiencia, resumen, bloques y sources.
  it("exporta todos los artículos con sus campos", () => {
    const catalog = buildDrFlitManualCatalog();

    expect(catalog.schemaVersion).toBe(DR_FLIT_MANUAL_CATALOG_SCHEMA_VERSION);
    expect(catalog.articleCount).toBe(MANUAL_ARTICLES.length);
    expect(catalog.articles.map((a) => a.slug)).toEqual(MANUAL_ARTICLES.map((a) => a.slug));

    for (const [i, source] of MANUAL_ARTICLES.entries()) {
      const exported = catalog.articles[i]!;
      expect(exported.title).toBe(source.title);
      expect(exported.audience).toBe(source.audience);
      expect(exported.summary).toBe(source.summary);
      expect(exported.href).toBe(`/manual/${source.slug}`);
      expect(exported.blocks.map((b) => b.paragraphs)).toEqual(source.blocks.map((b) => b.paragraphs));
      expect(exported.sources.map((s) => s.href)).toEqual((source.sources ?? []).map((s) => s.href));
    }
  });

  it("los slugs son únicos (el backend valida citas por slug)", () => {
    const slugs = buildDrFlitManualCatalog().articles.map((a) => a.slug);
    expect(new Set(slugs).size).toBe(slugs.length);
  });

  // AC3 — un artículo nuevo aparece sin tocar el script.
  it("incluye un artículo nuevo sin cambiar el generador", () => {
    const nuevo: ManualArticle = {
      slug: "9-prueba/articulo-nuevo",
      title: "Artículo nuevo",
      audience: "Todos",
      sectionId: "prueba",
      keywords: ["nuevo"],
      summary: "Resumen del artículo nuevo.",
      blocks: [{ id: "b1", title: "Bloque", paragraphs: ["Párrafo."] }],
    };

    const catalog = buildDrFlitManualCatalog([...MANUAL_ARTICLES, nuevo]);

    expect(catalog.articleCount).toBe(MANUAL_ARTICLES.length + 1);
    expect(catalog.articles.at(-1)).toEqual({
      slug: "9-prueba/articulo-nuevo",
      title: "Artículo nuevo",
      href: "/manual/9-prueba/articulo-nuevo",
      audience: "Todos",
      sectionId: "prueba",
      summary: "Resumen del artículo nuevo.",
      primarySource: false,
      blocks: [{ id: "b1", title: "Bloque", paragraphs: ["Párrafo."], bullets: [], callouts: [] }],
      sources: [],
    });
  });

  it("es determinista: sin marca de tiempo y con salto de línea final", () => {
    const text = serializeDrFlitManualCatalog();

    expect(serializeDrFlitManualCatalog()).toBe(text);
    expect(text.endsWith("}\n")).toBe(true);
    expect(text).not.toMatch(/generatedAt|\d{4}-\d{2}-\d{2}T\d{2}:/);
  });

  it("normaliza opcionales ausentes a valores explícitos", () => {
    const [article] = buildDrFlitManualCatalog([
      {
        slug: "x",
        title: "X",
        audience: "Gestor",
        sectionId: "s",
        keywords: [],
        summary: "S",
        blocks: [{ id: "b", title: "B", paragraphs: [], callouts: [{ variant: "tip", text: "t" }] }],
        sources: [{ title: "Norma", href: "/legal/n.pdf", kind: "pdf" }],
      },
    ]).articles;

    expect(article!.primarySource).toBe(false);
    expect(article!.blocks[0]!.callouts).toEqual([{ variant: "tip", title: null, text: "t" }]);
    expect(article!.sources).toEqual([{ title: "Norma", href: "/legal/n.pdf", kind: "pdf", ref: null }]);
  });
});
