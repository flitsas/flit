/**
 * HU #12920 (Épica #12718, ADR-0060 §3) — artefacto JSON del manual para el backend de DR. FLIT.
 *
 * El LLM del chat vive en core-api y necesita el manual como texto, sin depender de que el frontend
 * esté arriba. Por eso se exporta a JSON y se commitea en dos lugares: `public/dr-flit/` (estático del
 * frontend) y `services/core-api/src/Flit.Api/Content/dr-flit/` (content root del backend).
 *
 * `lib/manual/` sigue siendo la única fuente de verdad: este módulo solo lee `MANUAL_ARTICLES`.
 * La salida es determinista (sin marcas de tiempo, orden del catálogo, claves en orden fijo): el test
 * `generated-catalog-freshness.test.ts` la regenera en memoria y la compara byte a byte con lo
 * commiteado, y el backend la usa como bloque cacheable del prompt.
 */
import { MANUAL_ARTICLES } from "./articles";
import type { ManualArticle, ManualAudience, ManualCallout, ManualSource } from "./types";

/** Sube si cambia la forma del JSON: el backend rechaza versiones que no conoce. */
export const DR_FLIT_MANUAL_CATALOG_SCHEMA_VERSION = 1;

/** Rutas del artefacto relativas a la raíz del repo. */
export const DR_FLIT_MANUAL_CATALOG_PATHS = [
  "frontend/public/dr-flit/manual-catalog.generated.json",
  "services/core-api/src/Flit.Api/Content/dr-flit/manual-catalog.generated.json",
] as const;

export type DrFlitExportedBlock = {
  id: string;
  title: string;
  paragraphs: string[];
  bullets: string[];
  callouts: { variant: ManualCallout["variant"]; title: string | null; text: string }[];
};

export type DrFlitExportedArticle = {
  slug: string;
  title: string;
  href: string;
  audience: ManualAudience;
  sectionId: string;
  summary: string;
  primarySource: boolean;
  blocks: DrFlitExportedBlock[];
  sources: { title: string; href: string; kind: ManualSource["kind"]; ref: string | null }[];
};

export type DrFlitManualCatalogExport = {
  schemaVersion: number;
  generatedFrom: string;
  articleCount: number;
  articles: DrFlitExportedArticle[];
};

function exportArticle(article: ManualArticle): DrFlitExportedArticle {
  // Claves construidas una por una y en orden fijo: el JSON no depende del orden en que alguien
  // escribió las propiedades del artículo.
  return {
    slug: article.slug,
    title: article.title,
    href: `/manual/${article.slug}`,
    audience: article.audience,
    sectionId: article.sectionId,
    summary: article.summary,
    primarySource: article.primarySource ?? false,
    blocks: article.blocks.map((block) => ({
      id: block.id,
      title: block.title,
      paragraphs: [...block.paragraphs],
      bullets: [...(block.bullets ?? [])],
      callouts: (block.callouts ?? []).map((c) => ({ variant: c.variant, title: c.title ?? null, text: c.text })),
    })),
    sources: (article.sources ?? []).map((s) => ({ title: s.title, href: s.href, kind: s.kind, ref: s.ref ?? null })),
  };
}

export function buildDrFlitManualCatalog(
  articles: readonly ManualArticle[] = MANUAL_ARTICLES,
): DrFlitManualCatalogExport {
  return {
    schemaVersion: DR_FLIT_MANUAL_CATALOG_SCHEMA_VERSION,
    generatedFrom: "frontend/lib/manual (pnpm manual:export)",
    articleCount: articles.length,
    articles: articles.map(exportArticle),
  };
}

/** Texto exacto del artefacto: JSON con sangría de 2 espacios, LF y salto de línea final. */
export function serializeDrFlitManualCatalog(
  articles: readonly ManualArticle[] = MANUAL_ARTICLES,
): string {
  return `${JSON.stringify(buildDrFlitManualCatalog(articles), null, 2)}\n`;
}
