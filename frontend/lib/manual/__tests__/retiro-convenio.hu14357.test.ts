// HU #14357 AC6 — el manual ya no enseña el convenio de Compañías, conserva cómo firma el mandatario
// y los dos catálogos generados para DR. FLIT coinciden con los artículos.
//
// Uso de ejemplo:
//   pnpm exec vitest run lib/manual/__tests__/retiro-convenio.hu14357.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { ADMIN_COMPANY_ARTICLES } from "@/lib/manual/articles/admin-company";
import { SUPERADMIN_ARTICLES } from "@/lib/manual/articles/superadmin";
import {
  DR_FLIT_MANUAL_CATALOG_PATHS,
  serializeDrFlitManualCatalog,
} from "@/lib/manual/dr-flit-catalog-export";
import type { ManualArticle } from "@/lib/manual/types";

const REPO_ROOT = path.resolve(__dirname, "../../../..");

function textoDe(articulos: readonly ManualArticle[]): string {
  return JSON.stringify(articulos);
}

const mandatarios = ADMIN_COMPANY_ARTICLES.find(
  (a) => a.slug === "3-admin-company/2-representantes-mandatarios",
)!;

describe("HU #14357 AC6 — manual sin convenio de Compañías", () => {
  it("Administrador de compañía: no queda sección ni mención de convenio", () => {
    expect(mandatarios.blocks.map((b) => b.id)).not.toContain("convenio");
    expect(textoDe(ADMIN_COMPANY_ARTICLES)).not.toMatch(/convenio/i);
  });

  it("Super Admin: Convenio deja de figurar entre los filtros de la tabla", () => {
    const texto = textoDe(SUPERADMIN_ARTICLES);
    expect(texto).toContain("(Organismo, Código, Estado; en Bloqueos, Estado en red)");
    expect(texto).not.toMatch(/convenio/i);
  });

  it("sigue explicando cómo firma el mandatario con ambas formas y el caso «Sin firmar»", () => {
    const parrafos = mandatarios.blocks.flatMap((b) => b.paragraphs);
    const firma = parrafos.find((p) => p.includes("«Sin firmar»"));

    expect(firma).toBeDefined();
    expect(firma).toContain("con Baúl de firmas, la imagen de la firma del baúl");
    expect(firma).toContain("con Validación de identidad, el trazo de la firma");
    expect(firma).toContain("documento, UUID, firma y fecha de aprobación");
    expect(firma).toMatch(/^Persona natural:/);
  });

  it("Persona jurídica: sin recuadro del mandatario, firma solo el mandante y sus datos siguen en el cuerpo", () => {
    const parrafos = mandatarios.blocks.find((b) => b.id === "firma-mandatario")!.paragraphs;

    expect(parrafos).toContain(
      "Persona jurídica: el contrato no lleva recuadro del mandatario y firma solo el mandante; los datos del mandatario siguen en el cuerpo del contrato.",
    );
  });

  it("Formato en blanco: el recuadro queda con líneas para firmar a mano", () => {
    const parrafos = mandatarios.blocks.find((b) => b.id === "firma-mandatario")!.paragraphs;

    expect(parrafos).toContain("Formato en blanco: el recuadro queda con líneas para firmar a mano.");
  });

  it.each(DR_FLIT_MANUAL_CATALOG_PATHS)("el catálogo %s no trae la sección y coincide con los artículos", (relativa) => {
    const catalogo = readFileSync(path.join(REPO_ROOT, relativa), "utf8").replace(/\r\n/g, "\n");

    expect(catalogo).not.toContain("Convenio con el organismo");
    expect(catalogo).not.toContain("Estado, Convenio;");
    expect(catalogo).toContain("«Sin firmar»");
    expect(catalogo).toBe(serializeDrFlitManualCatalog());
  });
});
