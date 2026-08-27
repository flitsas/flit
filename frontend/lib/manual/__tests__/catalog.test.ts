import { describe, expect, it } from "vitest";
import {
  getArticleBySlug,
  MANUAL_HOME_SLUG,
  searchManualArticles,
} from "@/lib/manual/catalog";

describe("manual/catalog", () => {
  it("resuelve el artículo de bienvenida", () => {
    const article = getArticleBySlug(MANUAL_HOME_SLUG);
    expect(article?.title).toContain("Centro de Ayuda");
  });

  it("encuentra artículos de crear trámite", () => {
    const hits = searchManualArticles("como creo un tramite");
    expect(hits.length).toBeGreaterThan(0);
    expect(hits[0]?.href).toContain("/manual/");
    expect(hits.some((h) => h.slug.includes("crear-tramite"))).toBe(true);
  });

  it("encuentra preasignación OT", () => {
    const hits = searchManualArticles("preasignacion de placas");
    expect(hits.some((h) => h.slug.includes("preasignacion"))).toBe(true);
  });
});
