import { existsSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { MANUAL_ARTICLES } from "@/lib/manual/catalog";
import type { ManualMedia } from "@/lib/manual/types";

/**
 * HU #13012 (Épica #12755) — guardas del contenido visual del manual.
 * AC2: el alt es obligatorio en todo medio. AC3: todo asset referenciado existe en `public/`.
 * Hoy pueden no existir medios declarados; las guardas protegen cada artículo que los adopte.
 */

const PUBLIC_DIR = join(__dirname, "..", "..", "..", "public");

type MediaConDueno = { articleSlug: string; blockId: string; media: ManualMedia };

const allMedia: MediaConDueno[] = MANUAL_ARTICLES.flatMap((article) =>
  article.blocks.flatMap((block) =>
    (block.media ?? []).map((media) => ({ articleSlug: article.slug, blockId: block.id, media })),
  ),
);

describe("manual/media", () => {
  it("AC2 — todo medio declara un alt con contenido", () => {
    const sinAlt = allMedia.filter(({ media }) => media.alt.trim().length === 0);
    expect(
      sinAlt.map((m) => `${m.articleSlug}#${m.blockId}`),
      "el alt es obligatorio: es la accesibilidad de la figura y lo único que viaja al LLM",
    ).toEqual([]);
  });

  it("AC3 — toda imagen vive bajo /manual/ y su archivo existe en public/", () => {
    const imagenes = allMedia.filter(
      (m): m is MediaConDueno & { media: Extract<ManualMedia, { kind: "image" }> } =>
        m.media.kind === "image",
    );
    const fueraDeRuta = imagenes.filter(({ media }) => !media.src.startsWith("/manual/"));
    expect(
      fueraDeRuta.map((m) => `${m.articleSlug}#${m.blockId} → ${m.media.src}`),
      "las capturas del manual viven bajo public/manual/",
    ).toEqual([]);

    const rotas = imagenes.filter(({ media }) => !existsSync(join(PUBLIC_DIR, media.src)));
    expect(
      rotas.map((m) => `${m.articleSlug}#${m.blockId} → ${m.media.src}`),
      "asset referenciado que no existe en public/ (¿faltó correr el pipeline de capturas?)",
    ).toEqual([]);
  });

  it("AC3 — todo diagrama tiene id sano y su SVG compilado existe", () => {
    const diagramas = allMedia.filter(
      (m): m is MediaConDueno & { media: Extract<ManualMedia, { kind: "diagram" }> } =>
        m.media.kind === "diagram",
    );
    const idInvalido = diagramas.filter(({ media }) => !/^[a-z0-9]+(-[a-z0-9]+)*$/.test(media.id));
    expect(
      idInvalido.map((m) => `${m.articleSlug}#${m.blockId} → ${m.media.id}`),
      "el id del diagrama es kebab-case: nombra el .mmd fuente y el .svg compilado",
    ).toEqual([]);

    const sinSvg = diagramas.filter(
      ({ media }) => !existsSync(join(PUBLIC_DIR, "manual", "diagrams", `${media.id}.svg`)),
    );
    expect(
      sinSvg.map((m) => `${m.articleSlug}#${m.blockId} → ${m.media.id}.svg`),
      "diagrama declarado sin SVG compilado (¿faltó correr el build de diagramas?)",
    ).toEqual([]);
  });

  it("un caption declarado nunca queda vacío", () => {
    const vacios = allMedia.filter(
      ({ media }) => media.caption !== undefined && media.caption.trim().length === 0,
    );
    expect(vacios.map((m) => `${m.articleSlug}#${m.blockId}`)).toEqual([]);
  });
});
