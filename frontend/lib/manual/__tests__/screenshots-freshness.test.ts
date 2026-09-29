import { existsSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { getArticleBySlug } from "@/lib/manual/catalog";
import manifest from "@/lib/manual/screenshots.manifest.json";

/**
 * HU #13013 (Épica #12755) — guarda de frescura del pipeline de capturas.
 * AC2: agregar una entrada al manifiesto sin regenerar (o borrar una captura sin quitar su
 * entrada) rompe la suite listando faltantes y huérfanas. También valida el esquema del
 * manifiesto para que el pipeline nunca falle por datos malformados.
 */

const SCREENSHOTS_DIR = join(__dirname, "..", "..", "..", "public", "manual", "screenshots");
const PERFILES_VALIDOS = ["public", "gestor", "admin_company", "ot_admin", "superadmin"];

const entries = manifest.entries;

describe("manual/screenshots — manifiesto", () => {
  it("los ids son únicos y kebab-case (nombran el .png generado)", () => {
    const malos = entries.filter((e) => !/^[a-z0-9]+(-[a-z0-9]+)*$/.test(e.id));
    expect(malos.map((e) => e.id)).toEqual([]);
    const ids = entries.map((e) => e.id);
    expect(new Set(ids).size, "ids duplicados en el manifiesto").toBe(ids.length);
  });

  it("cada entrada apunta a un perfil válido y a una ruta absoluta de la app", () => {
    const perfilMalo = entries.filter((e) => !PERFILES_VALIDOS.includes(e.profile));
    expect(perfilMalo.map((e) => `${e.id} → ${e.profile}`)).toEqual([]);
    const rutaMala = entries.filter((e) => !e.route.startsWith("/"));
    expect(rutaMala.map((e) => `${e.id} → ${e.route}`)).toEqual([]);
  });

  it("cada entrada pertenece a un artículo real del catálogo", () => {
    const huerfanas = entries.filter((e) => getArticleBySlug(e.articleSlug) === undefined);
    expect(
      huerfanas.map((e) => `${e.id} → ${e.articleSlug}`),
      "la captura debe colgar de un artículo existente",
    ).toEqual([]);
  });
});

describe("manual/screenshots — frescura (AC2)", () => {
  it("toda entrada del manifiesto tiene su .png commiteado", () => {
    const faltantes = entries.filter(
      (e) => !existsSync(join(SCREENSHOTS_DIR, `${e.id}.png`)),
    );
    expect(
      faltantes.map((e) => `${e.id}.png`),
      "capturas declaradas sin generar: corre pnpm manual:screenshots y commitea el resultado",
    ).toEqual([]);
  });

  it("no hay capturas huérfanas (archivo sin entrada en el manifiesto)", () => {
    const declaradas = new Set(entries.map((e) => `${e.id}.png`));
    const archivos = existsSync(SCREENSHOTS_DIR)
      ? readdirSync(SCREENSHOTS_DIR).filter((f) => f.endsWith(".png"))
      : [];
    const huerfanos = archivos.filter((f) => !declaradas.has(f));
    expect(
      huerfanos,
      "capturas sin dueño en el manifiesto: agrégalas al manifiesto o bórralas",
    ).toEqual([]);
  });
});
