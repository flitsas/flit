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
import { MANDATO_TIPOS } from "@/lib/plataforma/mandato-templates";

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
    const firma = parrafos.find((p) => p.startsWith("Persona natural:"));

    expect(firma).toBeDefined();
    expect(firma).toContain("con Baúl de firmas, la imagen de la firma del baúl");
    expect(firma).toContain("con Validación de identidad, el trazo de la firma");
    expect(firma).toContain("documento, UUID, firma y fecha de aprobación");
    expect(firma).toMatch(/^Persona natural:/);
  });

  // Modelo del mandatario (lo elige el Administrador) y tipo de mandato (lo fija Plataforma) son dos ejes:
  // lo que quita el recuadro es el tipo Institucional, no el modelo Persona jurídica.
  describe("firma según modelo y tipo de mandato (MandatoFirmaModoResolver)", () => {
    const parrafos = () => mandatarios.blocks.find((b) => b.id === "firma-mandatario")!.paragraphs;
    const parrafoDe = (inicio: string) => parrafos().find((p) => p.startsWith(inicio));

    it("distingue modelo y tipo con los rótulos reales de Plataforma", () => {
      const texto = parrafos().join(" ");
      expect(texto).toContain("el modelo, que eliges al registrar al mandatario");
      expect(texto).toContain("el tipo de mandato, que fija Plataforma");
      for (const { label } of MANDATO_TIPOS) {
        expect(texto).toContain(`«${label}»`);
      }
      expect(texto).toContain("salvo que la configuración de mandato del organismo disponga otra cosa");
    });

    it("caso 1, tipo por defecto + Persona natural: firma con Baúl o Validación de identidad, o «Sin firmar»", () => {
      const natural = parrafoDe("Persona natural:");
      expect(natural).toContain("con Baúl de firmas, la imagen de la firma del baúl con su vigencia y código hash");
      expect(natural).toContain("con Validación de identidad, el trazo de la firma");
      expect(natural).toContain("el recuadro queda «Sin firmar»");
    });

    it("caso 2, tipo por defecto + Persona jurídica: SÍ lleva recuadro; «Sin firmar» o la firma del baúl registrada con su NIT", () => {
      const juridica = parrafoDe("Persona jurídica:");
      expect(juridica).not.toMatch(/no lleva recuadro|firma solo el mandante/);
      expect(juridica).toContain("el recuadro sale «Sin firmar», con la línea para firmar a mano");
      expect(juridica).toContain("una firma vigente registrada con el NIT de la entidad, esa firma se estampa");
    });

    it("caso 3, tipo Institucional: sin recuadro, firma solo el mandante y el organismo en el cuerpo según el formato", () => {
      const institucional = parrafos().find((p) => p.includes("Con «Institucional (organismo)»"));
      expect(institucional).toContain("el contrato no lleva recuadro del mandatario y firma solo el mandante");
      expect(institucional).toContain("con su nombre y NIT si el formato de contrato los incluye");
    });

    it("caso 4, Formato en blanco y tipo Abierto no son lo mismo: uno es modelo y el otro tipo", () => {
      const blanco = parrafoDe("Formato en blanco:");
      expect(blanco).toContain("el recuadro sale «Sin firmar», con la línea para firmar a mano");
      const abierto = parrafos().find((p) => p.includes("Con «Abierto (sin mandatario)»"));
      expect(abierto).toContain("nombre, documento y firma quedan en líneas en blanco");
      expect(abierto).toContain("en el cuerpo y en el recuadro");
      // Con datos institucionales del organismo, el recuadro sale con su razón social y Bello la nombra en el cuerpo.
      expect(abierto).toContain("si el organismo tiene datos institucionales, pueden aparecer en el contrato");
      expect(abierto).toContain("El recuadro también lleva la marca «Sin firmar».");
      expect(abierto).not.toBe(blanco);
    });
  });

  it.each(DR_FLIT_MANUAL_CATALOG_PATHS)("el catálogo %s no trae la sección y coincide con los artículos", (relativa) => {
    const catalogo = readFileSync(path.join(REPO_ROOT, relativa), "utf8").replace(/\r\n/g, "\n");

    expect(catalogo).not.toContain("Convenio con el organismo");
    expect(catalogo).not.toContain("Estado, Convenio;");
    expect(catalogo).toContain("«Sin firmar»");
    expect(catalogo).toBe(serializeDrFlitManualCatalog());
  });
});
