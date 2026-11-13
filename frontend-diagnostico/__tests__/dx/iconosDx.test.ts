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
  "IconoDxLocatario",
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

  it("la carpeta solo tiene la base, los íconos y la licencia de los glifos copiados (ningún archivo de mapa nombre→componente)", () => {
    const otros = archivos.filter(
      (n) => n !== "BaseIconoDx.tsx" && n !== "LICENSE-ant-design-icons-svg.txt" && !iconos.includes(n),
    );
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

// HU #14562, auditoría de UI (§4.1, m-14): Licencia se distingue de RUNT a 16–24 px y Locatario sigue el glifo
// `f-locatario` de la maqueta (persona con un documento).
describe("HU14562 · glifos de Licencia y Locatario", () => {
  const glifo = (archivo: string) => {
    const codigo = readFileSync(join(DIR, `${archivo}.tsx`), "utf8");
    return [...codigo.matchAll(/<(rect|circle|path)\s[^>]*\/>/g)].map((m) => m[0].replace(/\s+/g, " "));
  };

  it("IconoDxLicencia no repite el avatar ni el marco de IconoDxRunt", () => {
    const runt = glifo("IconoDxRunt");
    const licencia = glifo("IconoDxLicencia");
    expect(licencia.length).toBeGreaterThan(0);
    expect(licencia.filter((forma) => runt.includes(forma))).toEqual([]);
    expect(licencia.join(" ")).not.toContain('cx="336" cy="448"');
  });

  it("IconoDxLocatario es la persona con documento de la maqueta (f-locatario)", () => {
    expect(glifo("IconoDxLocatario")).toEqual([
      '<circle cx="384" cy="320" r="160" />',
      '<path d="M128 864c0-176 112-288 256-288 48 0 96 12 136 36" />',
      '<rect x="608" y="560" width="288" height="336" rx="48" />',
      '<path d="M688 688h128M688 800h80" />',
    ]);
  });
});
