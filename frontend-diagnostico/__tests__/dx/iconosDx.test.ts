import { readdirSync, readFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { describe, expect, it } from "vitest";
import * as ui from "@flit/ui";

// Uso de ejemplo: import { IconoDxRunt } from "@/components/dx/iconos/IconoDxRunt"; — un archivo y un export por ícono,
// sin barril ni mapa, para que el build descarte los que la pantalla no usa (D2 = c, HU #14562 · UI-3).

const DIR = resolve(__dirname, "../../components/dx/iconos");
const ESPERADOS = [
  "IconoDxRunt",
  "IconoDxRuntPersona",
  "IconoDxSimit",
  "IconoDxSimitPersona",
  "IconoDxAvaluo",
  "IconoDxImpuesto",
  "IconoDxRues",
  "IconoDxLicencia",
  "IconoDxEjecutado",
  "IconoDxDeuda",
  "IconoDxError",
  "IconoDxEnCurso",
  "IconoDxPendiente",
  "IconoDxNovedad",
  "IconoDxManual",
  "IconoDxCampana",
  "IconoDxDescargarZip",
  "IconoDxCrearTramite",
  "IconoDxDictamen",
];

const archivos = readdirSync(DIR);
const iconos = archivos.filter((n) => /^IconoDx\w+\.tsx$/.test(n));

describe("AC3 · familia IconoDx* con exportación por ícono", () => {
  it("existe un archivo por cada ícono de la familia", () => {
    for (const nombre of ESPERADOS) expect(archivos, nombre).toContain(`${nombre}.tsx`);
  });

  it("no hay index.ts de barril", () => {
    expect(archivos.filter((n) => /^index\.(ts|tsx|js)$/.test(n))).toEqual([]);
  });

  it("la carpeta solo tiene la base y los íconos (ningún archivo de mapa nombre→componente)", () => {
    const otros = archivos.filter((n) => n !== "BaseIconoDx.tsx" && !iconos.includes(n));
    expect(otros).toEqual([]);
  });

  it("cada IconoDx<Nombre>.tsx exporta exactamente un componente, con el nombre del archivo", async () => {
    expect(iconos.length).toBe(ESPERADOS.length);
    for (const archivo of iconos) {
      const nombre = archivo.replace(/\.tsx$/, "");
      const modulo = (await import(join(DIR, archivo))) as Record<string, unknown>;
      expect(Object.keys(modulo), archivo).toEqual([nombre]);
      expect(typeof modulo[nombre], archivo).toBe("function");
    }
  });

  it("ningún ícono declara un mapa ni un objeto de íconos", () => {
    for (const archivo of iconos) {
      const codigo = readFileSync(join(DIR, archivo), "utf8");
      expect(codigo, archivo).not.toMatch(/Record<|export\s+default|export\s+const\s+\w+\s*=\s*\{/);
    }
  });

  it("ningún nombre coincide con un export de @flit/ui (Icono, IconoModulo…)", () => {
    const exportsUi = Object.keys(ui);
    expect(exportsUi).toContain("Icono");
    for (const archivo of [...iconos, "BaseIconoDx.tsx"]) {
      expect(exportsUi, archivo).not.toContain(archivo.replace(/\.tsx$/, ""));
    }
  });

  it("los íconos no traen colores sueltos: trazo en currentColor y acento por token --dx-icono-*", () => {
    for (const archivo of [...iconos, "BaseIconoDx.tsx"]) {
      const codigo = readFileSync(join(DIR, archivo), "utf8");
      expect(codigo, archivo).not.toMatch(/["'`[(]#[0-9a-fA-F]{3,8}\b/);
      expect(codigo, archivo).not.toMatch(/rgba?\(/);
    }
  });
});
