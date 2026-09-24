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

  // HU #12851 (Feature #12846) — la preasignación se retiró del manual; se prueba con un artículo
  // OT que sigue vigente.
  it("encuentra validar impronta OT", () => {
    const hits = searchManualArticles("validar impronta");
    expect(hits.some((h) => h.slug.includes("validar-impronta"))).toBe(true);
  });
});
