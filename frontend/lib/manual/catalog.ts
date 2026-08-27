/**
 * Catálogo del Centro de Ayuda FLIT — utilidades de navegación y búsqueda.
 * Contenido en lib/manual/articles/.
 */

export type {
  ManualArticle,
  ManualAudience,
  ManualCallout,
  ManualNavSection,
  ManualSectionBlock,
} from "./types";

export { MANUAL_ARTICLES } from "./articles";
export { MANUAL_HOME_SLUG, MANUAL_NAV_SECTIONS } from "./articles/meta";

import { MANUAL_ARTICLES } from "./articles";
import { MANUAL_NAV_SECTIONS } from "./articles/meta";
import type { ManualArticle, ManualNavSection } from "./types";

export function getArticleBySlug(slug: string): ManualArticle | undefined {
  const normalized = slug.replace(/^\/+|\/+$/g, "");
  return MANUAL_ARTICLES.find((a) => a.slug === normalized);
}

export function getNavTree(): {
  section: ManualNavSection;
  articles: ManualArticle[];
}[] {
  return MANUAL_NAV_SECTIONS.map((section) => ({
    section,
    articles: MANUAL_ARTICLES.filter((a) => a.sectionId === section.id),
  }));
}

export function getAdjacentArticles(slug: string): {
  prev: ManualArticle | null;
  next: ManualArticle | null;
} {
  const idx = MANUAL_ARTICLES.findIndex((a) => a.slug === slug);
  if (idx < 0) return { prev: null, next: null };
  return {
    prev: idx > 0 ? MANUAL_ARTICLES[idx - 1]! : null,
    next: idx < MANUAL_ARTICLES.length - 1 ? MANUAL_ARTICLES[idx + 1]! : null,
  };
}

export type ManualSearchHit = {
  slug: string;
  title: string;
  audience: ManualArticle["audience"];
  summary: string;
  href: string;
  score: number;
};

const SEARCH_STOPWORDS = new Set([
  "el",
  "la",
  "los",
  "las",
  "de",
  "del",
  "un",
  "una",
  "y",
  "o",
  "en",
  "a",
  "al",
  "para",
  "por",
  "con",
  "que",
  "como",
  "the",
  "and",
  "for",
  "no",
  "si",
  "me",
  "mi",
  "se",
  "es",
  "son",
  "hay",
]);

function tokenize(q: string): string[] {
  return q
    .toLowerCase()
    .normalize("NFD")
    .replace(/\p{M}/gu, "")
    .split(/[^a-z0-9]+/i)
    .filter((t) => t.length >= 3 && !SEARCH_STOPWORDS.has(t));
}

function articleHaystack(article: ManualArticle): string {
  return [
    article.title,
    article.summary,
    ...article.keywords,
    ...article.blocks.flatMap((b) => [
      b.title,
      ...b.paragraphs,
      ...(b.bullets ?? []),
      ...(b.callouts?.map((c) => `${c.title ?? ""} ${c.text}`) ?? []),
    ]),
  ].join(" ");
}

/** Match por keywords/título para búsqueda del manual y DR-FLIT. */
export function searchManualArticles(query: string, limit = 8): ManualSearchHit[] {
  const tokens = tokenize(query);
  if (tokens.length === 0) return [];

  const hits: ManualSearchHit[] = [];
  for (const article of MANUAL_ARTICLES) {
    const titleNorm = article.title
      .toLowerCase()
      .normalize("NFD")
      .replace(/\p{M}/gu, "");
    const keywordsNorm = article.keywords.map((k) =>
      k
        .toLowerCase()
        .normalize("NFD")
        .replace(/\p{M}/gu, ""),
    );
    const haystack = articleHaystack(article)
      .toLowerCase()
      .normalize("NFD")
      .replace(/\p{M}/gu, "");

    let score = 0;
    let strongHits = 0;
    for (const token of tokens) {
      const inKeyword = keywordsNorm.some((k) => k.includes(token) || token.includes(k));
      const inTitle = titleNorm.includes(token);
      if (inKeyword) {
        score += 4;
        strongHits += 1;
      } else if (inTitle) {
        score += 3;
        strongHits += 1;
      } else if (haystack.includes(token)) {
        score += 1;
      }
    }
    if (score >= 3 && strongHits > 0) {
      hits.push({
        slug: article.slug,
        title: article.title,
        audience: article.audience,
        summary: article.summary,
        href: `/manual/${article.slug}`,
        score,
      });
    }
  }

  return hits.sort((a, b) => b.score - a.score || a.title.localeCompare(b.title)).slice(0, limit);
}
