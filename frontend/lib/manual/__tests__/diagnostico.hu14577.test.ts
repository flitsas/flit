import { describe, expect, it } from "vitest";

import { getArticleBySlug, getNavTree, MANUAL_ARTICLES } from "@/lib/manual/catalog";
import { MANUAL_NAV_SECTIONS, MANUAL_VERSION, MANUAL_VERSION_DATE } from "@/lib/manual/articles/meta";
import type { ManualAudience } from "@/lib/manual/types";

/**
 * HU #14577 — DR. FLIT documenta Diagnóstico vehicular y su saneamiento (ADR-0087).
 *
 * Uso de ejemplo:
 *   getArticleBySlug("6-diagnostico/10-saneamiento")?.audience  // "Gestor"
 *   getNavTree().find((n) => n.section.id === "diagnostico")     // la sección con sus artículos
 */

const ESPERADOS: { slug: string; audience: ManualAudience }[] = [
  { slug: "6-diagnostico/1-que-es", audience: "Gestor" },
  { slug: "6-diagnostico/2-inicio-y-vehiculos", audience: "Gestor" },
  { slug: "6-diagnostico/3-nuevo-diagnostico", audience: "Gestor" },
  { slug: "6-diagnostico/4-tablero-y-dictamen", audience: "Gestor" },
  { slug: "6-diagnostico/5-impuesto-departamental", audience: "Gestor" },
  { slug: "6-diagnostico/6-evidencias-y-avisos", audience: "Gestor" },
  { slug: "6-diagnostico/7-crear-tramite", audience: "Gestor" },
  { slug: "6-diagnostico/8-configuracion", audience: "Admin de Compañía" },
  { slug: "6-diagnostico/9-backoffice-flit", audience: "Super Admin" },
  { slug: "6-diagnostico/10-saneamiento", audience: "Gestor" },
  { slug: "6-diagnostico/11-saneamiento-gestion-flit", audience: "Super Admin" },
  { slug: "6-diagnostico/12-estados-y-preguntas", audience: "Gestor" },
];

const DIAGRAMAS = [
  "flujo-diagnostico-alta",
  "flujo-diagnostico-consulta-dictamen",
  "flujo-diagnostico-impuesto",
  "flujo-saneamiento",
];

const textoDe = (slug: string) => {
  const a = getArticleBySlug(slug)!;
  return [a.summary, ...a.blocks.flatMap((b) => [b.title, ...b.paragraphs, ...(b.bullets ?? []), ...(b.callouts ?? []).map((c) => c.text)])].join(" ");
};

describe("manual/diagnostico (HU #14577)", () => {
  it("hay una sección «Diagnóstico vehicular» después de Super Admin", () => {
    const ids = MANUAL_NAV_SECTIONS.map((s) => s.id);
    expect(ids).toContain("diagnostico");
    expect(MANUAL_NAV_SECTIONS.find((s) => s.id === "diagnostico")?.label).toBe("Diagnóstico vehicular");
    expect(ids.indexOf("diagnostico")).toBe(ids.indexOf("superadmin") + 1);
  });

  it("los artículos existen, en la sección y con la audiencia de cada perfil", () => {
    for (const e of ESPERADOS) {
      const a = getArticleBySlug(e.slug);
      expect(a, e.slug).toBeDefined();
      expect(a!.sectionId, e.slug).toBe("diagnostico");
      expect(a!.audience, e.slug).toBe(e.audience);
    }
    const tree = getNavTree().find((n) => n.section.id === "diagnostico");
    expect(tree?.articles.map((a) => a.slug)).toEqual(ESPERADOS.map((e) => e.slug));
  });

  it("los cuatro flujos principales tienen su diagrama en algún artículo de la sección", () => {
    const usados = new Set(
      MANUAL_ARTICLES.filter((a) => a.sectionId === "diagnostico").flatMap((a) =>
        a.blocks.flatMap((b) => (b.media ?? []).filter((m) => m.kind === "diagram").map((m) => (m as { id: string }).id)),
      ),
    );
    for (const id of DIAGRAMAS) expect(usados.has(id), id).toBe(true);
  });

  it("el saneamiento no cobra y SANEADO no cambia el dictamen; el renglón se quita sin borrarse", () => {
    const cliente = textoDe("6-diagnostico/10-saneamiento");
    expect(cliente).toMatch(/no cobra/i);
    expect(cliente).toMatch(/no cambia el dictamen/i);
    const flit = textoDe("6-diagnostico/11-saneamiento-gestion-flit");
    expect(flit).toMatch(/historial/i);
    expect(flit).toMatch(/Quitar concepto/);
    expect(flit).toMatch(/10 MB/);
  });

  it("el impuesto documenta vigencias, «Pagada», monto total y que no se llena a mano", () => {
    const t = textoDe("6-diagnostico/5-impuesto-departamental");
    for (const frase of ["Vigencias", "Pagada", "Monto total", "Departamento no soportado"]) expect(t).toContain(frase);
    expect(t).toMatch(/no se llena a mano/i);
  });

  it("sin marcadores pendientes ni placas que no sean de ejemplo", () => {
    for (const e of ESPERADOS) {
      const t = textoDe(e.slug);
      expect(t, e.slug).not.toMatch(/\b(TODO|TBD|XXX)\b/);
      const placas = t.match(/\b[A-Z]{3}\d{3}\b/g) ?? [];
      expect(placas.filter((p) => p !== "ABC123"), e.slug).toEqual([]);
    }
  });

  it("versión del manual 2.2.0 de noviembre de 2026", () => {
    expect(MANUAL_VERSION).toBe("2.2.0");
    expect(MANUAL_VERSION_DATE).toBe("Noviembre 2026");
  });
});
