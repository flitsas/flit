import { readdirSync, readFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { textoEstadoCelda } from "@/components/tablero/CeldaSemaforo";

// Uso de ejemplo: los specs de Playwright (e2e/) no corren en Vitest; esta guarda lee su texto y avisa cuando un selector
// sigue con un rótulo que la pantalla ya no muestra (HU #14562, hallazgo B1 de la auditoría de UI: «Ejecutado (OK)»).

const DIR = resolve(__dirname, "../e2e");
const archivos = readdirSync(DIR).filter((n) => /\.ts$/.test(n));
const texto = (nombre: string) => readFileSync(join(DIR, nombre), "utf8");

describe("HU14562 B1 · los E2E usan los rótulos vigentes del semáforo y de los botones", () => {
  it("ningún spec busca el rótulo viejo «Ejecutado (OK)»", () => {
    expect(textoEstadoCelda("COMPLETADO")).toBe("Ejecutado");
    for (const archivo of archivos) {
      expect(texto(archivo), archivo).not.toMatch(/Ejecutado \\?\(OK\\?\)/);
    }
  });

  it("el semáforo del impuesto acepta Ejecutado, Con deuda y Novedad", () => {
    const spec = texto("impuesto.spec.ts");
    expect(spec).toContain("(Ejecutado|Con deuda|Novedad) \\(se abre en una pestaña nueva\\)$");
  });

  it("el helper del Express usa «Diagnosticar en línea» y acepta «Iniciar diagnóstico» además de «Continuar»", () => {
    const helper = texto("express.ts");
    expect(helper).toContain('{ name: "Diagnosticar en línea" }');
    expect(helper).toContain("/^(Continuar|Iniciar diagnóstico)$/");
  });
});

// HU #14562 · AC55 (y MA-FE-1 del code review): cada uno de los 8 specs lleva en su cabecera la «lista congelada» de los
// nombres accesibles y los data-* de los que depende. La guarda lee el spec (y el helper express.ts si lo importa) y exige
// que cada nombre literal y cada data-* que usa aparezca en esa lista, entre «».
describe("HU14562 AC55 · lista congelada en la cabecera de los 8 E2E", () => {
  const SPECS = [
    "listado.spec.ts",
    "nuevo-diagnostico.spec.ts",
    "tablero.spec.ts",
    "evidencias.spec.ts",
    "impuesto.spec.ts",
    "configuracion.spec.ts",
    "backoffice-llenado.spec.ts",
    "entrada-desde-hub.spec.ts",
  ];
  const cabecera = (codigo: string) => codigo.slice(0, codigo.indexOf("*/"));
  /**
   * Nombres literales (comillas o plantilla) del name: de getByRole, de getByLabel( y de getByText(, y atributos data-*
   * de los selectores. El `name:` del archivo de setInputFiles no es un nombre accesible y no cuenta.
   */
  const usados = (codigo: string) => {
    const porRol = [...codigo.matchAll(/getByRole\("[a-z]+",\s*\{[^\n]*?name:\s*(["'`])((?:(?!\1).)+)\1/g)].map((m) => m[2]);
    const otros = [...codigo.matchAll(/(?:getByLabel\(|getByText\()\s*(["'`])((?:(?!\1).)+)\1/g)].map((m) => m[2]);
    const nombres = [...porRol, ...otros];
    const datos = [...codigo.matchAll(/\[(data-[a-z-]+)/g)].map((m) => m[1]);
    return [...new Set([...nombres, ...datos])];
  };

  it("los 8 specs existen", () => {
    for (const spec of SPECS) expect(archivos, spec).toContain(spec);
  });

  for (const spec of SPECS) {
    it(`${spec}: la cabecera trae la lista congelada con cada nombre y data-* que usa`, () => {
      const codigo = texto(spec);
      const lista = cabecera(codigo);
      expect(lista, spec).toContain("Lista congelada (HU #14562 · AC55)");
      const propios = usados(codigo.slice(codigo.indexOf("*/")));
      const delHelper = codigo.includes('from "./express"') ? usados(texto("express.ts")) : [];
      const faltan = [...propios, ...delHelper].filter((n) => !lista.includes(`«${n}»`));
      expect(faltan, `${spec}: faltan en la lista congelada`).toEqual([]);
    });
  }
});
