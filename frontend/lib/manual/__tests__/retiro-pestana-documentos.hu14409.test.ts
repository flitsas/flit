// HU #14409 AC6 — los manuales dejan de describir la pestaña Documentos de la ficha de compañía,
// conservan las pestañas homónimas y los dos catálogos de DR. FLIT coinciden con los artículos.
//
// Uso de ejemplo:
//   pnpm exec vitest run lib/manual/__tests__/retiro-pestana-documentos.hu14409.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { ADMIN_COMPANY_ARTICLES } from "@/lib/manual/articles/admin-company";
import { SUPERADMIN_ARTICLES } from "@/lib/manual/articles/superadmin";
import {
  DR_FLIT_MANUAL_CATALOG_PATHS,
  serializeDrFlitManualCatalog,
} from "@/lib/manual/dr-flit-catalog-export";

const REPO_ROOT = path.resolve(__dirname, "../../../..");

const bloque = (articulos: typeof SUPERADMIN_ARTICLES, id: string) =>
  articulos.flatMap((a) => a.blocks).find((b) => b.id === id)!;

describe("HU #14409 AC6 — manual del Super Admin", () => {
  const companias = bloque(SUPERADMIN_ARTICLES, "companias");

  it("la ficha ya no describe la pestaña Documentos", () => {
    expect((companias.bullets ?? []).filter((b) => b.startsWith("Documentos:"))).toEqual([]);
    expect(JSON.stringify(SUPERADMIN_ARTICLES)).not.toContain("Obligatorio, Opcional, Oculto o Base");
    expect(JSON.stringify(SUPERADMIN_ARTICLES)).not.toContain("Agregar parámetro");
  });

  it("el conteo de pestañas coincide con las viñetas que quedan", () => {
    expect(companias.paragraphs.join(" ")).toContain("con 7 pestañas");
    const pestanas = (companias.bullets ?? []).map((b) => b.split(":")[0]);
    expect(pestanas).toEqual([
      "Productos",
      "Trámites",
      "Configuración Empresa",
      "Representantes legales",
      "Mandatarios",
      "Usuarios",
      "Historial de Cambios",
    ]);
  });

  it("las pestañas homónimas siguen descritas", () => {
    const texto = JSON.stringify(SUPERADMIN_ARTICLES);
    expect(texto).toContain("Documentos (prelación y etiquetas)");
    expect(texto).toContain("tres vistas: Documentos (lo que exige el catálogo)");
    expect(texto).toContain("Documentos: qué exige el tipo; el detalle vive en Documental.");
  });
});

describe("HU #14409 AC6 — manual del Administrador de compañía", () => {
  const tusPestanas = bloque(ADMIN_COMPANY_ARTICLES, "entrar");
  const pideAFlit = bloque(ADMIN_COMPANY_ARTICLES, "pide-a-flit");

  it("la lista de lo que antes veía ya no nombra Documentos", () => {
    const parrafos = tusPestanas.paragraphs.join(" ");
    expect(parrafos).toContain("(Trámites, Configuración Empresa, Usuarios, Historial de Cambios)");
    expect(parrafos).not.toContain("Configuración Empresa, Documentos,");
  });

  it("los documentos personalizados siguen en su viñeta, con su nombre propio", () => {
    const bullets = pideAFlit.bullets ?? [];
    expect(bullets.filter((b) => b.startsWith("Documentos:"))).toEqual([]);
    expect(bullets.filter((b) => b.startsWith("Plantillas y documentos personalizados:"))).toHaveLength(1);
  });
});

describe("HU #14409 AC6 — catálogos generados", () => {
  it.each(DR_FLIT_MANUAL_CATALOG_PATHS)("%s no describe la pestaña y coincide con los artículos", (relativa) => {
    const catalogo = readFileSync(path.join(REPO_ROOT, relativa), "utf8").replace(/\r\n/g, "\n");

    expect(catalogo).not.toContain("Obligatorio, Opcional, Oculto o Base");
    expect(catalogo).toContain("Documentos (prelación y etiquetas)");
    expect(catalogo).toBe(serializeDrFlitManualCatalog());
  });
});
