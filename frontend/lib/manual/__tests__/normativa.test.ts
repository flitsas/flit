import { existsSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import {
  getArticleBySlug,
  MANUAL_NAV_SECTIONS,
  NORMATIVA_RESOLUCION_PDF_HREF,
  NORMATIVA_RESOLUCION_SLUG,
  searchManualArticles,
  visibleAudiences,
} from "@/lib/manual/catalog";
import type { ManualProfile } from "@/lib/manual/types";

const PERFILES: ManualProfile[] = ["gestor", "ot_admin", "admin_company", "superadmin"];

describe("manual/normativa — la resolución como fuente principal", () => {
  it("el artículo existe, aplica a Todos, está marcado como fuente principal y enlaza el PDF servido", () => {
    const a = getArticleBySlug(NORMATIVA_RESOLUCION_SLUG);
    expect(a).toBeDefined();
    expect(a!.audience).toBe("Todos");
    expect(a!.primarySource).toBe(true);
    expect(a!.sources?.[0]).toMatchObject({ kind: "pdf", href: NORMATIVA_RESOLUCION_PDF_HREF });
    expect(a!.title).toContain("20233040017145");
  });

  it("el PDF está en public/ (se sirve en /legal/…) y no es un archivo vacío", () => {
    const file = join(process.cwd(), "public", NORMATIVA_RESOLUCION_PDF_HREF);
    expect(existsSync(file), file).toBe(true);
    expect(statSync(file).size).toBeGreaterThan(500_000);
  });

  it("la sección Normativa va inmediatamente después de Introducción", () => {
    const ids = MANUAL_NAV_SECTIONS.map((s) => s.id);
    expect(ids.indexOf("normativa")).toBe(ids.indexOf("introduccion") + 1);
  });

  it("todo perfil la ve, y para preguntas normativas es el PRIMER resultado", () => {
    const preguntas = [
      "qué dice la norma sobre los trámites virtuales",
      "resolución 20233040017145",
      "resolución 17145 ministerio de transporte",
      "sustento legal de la preasignación de placa runt 60 días",
      "marco legal autenticación digital runt",
      "qué requisitos exige la norma para el traspaso",
    ];
    for (const perfil of PERFILES) {
      for (const q of preguntas) {
        const hits = searchManualArticles(q, 5, { audiences: visibleAudiences(perfil) });
        expect(hits[0]?.slug, `${perfil} · «${q}» → ${hits.map((h) => h.slug).join(", ")}`).toBe(
          NORMATIVA_RESOLUCION_SLUG,
        );
        expect(hits[0]?.sources?.[0]?.href).toBe(NORMATIVA_RESOLUCION_PDF_HREF);
      }
    }
  });

  it("no secuestra las preguntas operativas: el how-to del módulo sigue primero", () => {
    const casos: [string, ManualProfile, string][] = [
      ["cómo creo un trámite", "gestor", "1-gestor/2-crear-tramite"],
      ["liberar placa", "ot_admin", "2-ot/1-tramites-bandeja"],
      ["invitar usuario", "gestor", "1-gestor/12-usuarios"],
      ["red de clientes", "admin_company", "3-admin-company/3-red-de-clientes"],
    ];
    for (const [q, perfil, slug] of casos) {
      const hits = searchManualArticles(q, 5, { audiences: visibleAudiences(perfil) });
      expect(hits[0]?.slug, `«${q}» → ${hits.map((h) => h.slug).join(", ")}`).toBe(slug);
    }
  });

  it("cuando la norma también aplica a una duda operativa, acompaña al how-to (no lo desplaza)", () => {
    const hits = searchManualArticles("preasignación de placa", 5, {
      audiences: visibleAudiences("gestor"),
    });
    const slugs = hits.map((h) => h.slug);
    expect(slugs).toContain("1-gestor/7-ruta-placa");
    expect(slugs).toContain(NORMATIVA_RESOLUCION_SLUG);
  });
});
