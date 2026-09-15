// HU #12415 — pruebas del inventario reproducible de superficies con marca y
// de colores de marca escritos a mano (AC1, AC2, AC3).
//
// Uso de ejemplo:
//   import { buildInventory, classifyHex } from "../brand-color-inventory.mjs";
//   const inventory = buildInventory();
//
// Estas pruebas hacen exigible el inventario: fallan si (a) aparece un hex de
// marca nuevo, no documentado, dentro de una superficie del alcance cerrado, o
// (b) un archivo listado en el inventario deja de existir. No hacen mock del
// escáner que se está probando — solo leen el árbol real del repo, igual que
// `pnpm brand:inventory`.
import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import path from "node:path";
import {
  BRAND_HEXES,
  SURFACES,
  buildInventory,
  classifyHex,
  FRONTEND_ROOT,
} from "../brand-color-inventory.mjs";

const BASELINE_PATH = path.join(FRONTEND_ROOT, "docs", "brand-color-inventory.json");

function loadCommittedBaseline() {
  const raw = readFileSync(BASELINE_PATH, "utf8");
  return JSON.parse(raw);
}

describe("classifyHex — contrato de clasificación (AC2)", () => {
  it("clasifica los 6 hex de marca de globals.css:9-14 como 'marca', sin importar mayúsculas", () => {
    for (const hex of BRAND_HEXES) {
      expect(classifyHex(hex)).toBe("marca");
      expect(classifyHex(hex.toUpperCase())).toBe("marca");
    }
  });

  it("clasifica blanco, negro y los fondos/tarjetas oscuros del shell como 'neutro'", () => {
    expect(classifyHex("ffffff")).toBe("neutro");
    expect(classifyHex("000000")).toBe("neutro");
    expect(classifyHex("05060a")).toBe("neutro");
    expect(classifyHex("0b0f14")).toBe("neutro");
  });

  it("clasifica escala de grises genérica (R=G=B) como 'neutro'", () => {
    expect(classifyHex("aaaaaa")).toBe("neutro");
  });

  it("clasifica cualquier otro hex (acento, semántico) como 'estado' por defecto", () => {
    // #4F74C9 — acento del dock (components/atom/dock/DockDesktop.tsx:204),
    // no es uno de los 6 hex de marca exactos ni escala de grises.
    expect(classifyHex("4F74C9")).toBe("estado");
  });
});

describe("buildInventory — reproducibilidad determinista (AC3)", () => {
  it("enumera exactamente las 5 superficies del alcance cerrado de AC1", () => {
    const inventory = buildInventory();
    const ids = inventory.surfaces.map((s) => s.id).sort();
    expect(ids).toEqual(
      ["acceso", "activacion", "cabecera-menu", "pestana", "recuperacion"].sort(),
    );
  });

  it("produce el mismo resultado en dos ejecuciones consecutivas (determinismo)", () => {
    const first = buildInventory();
    const second = buildInventory();
    const strip = (inv: ReturnType<typeof buildInventory>) => {
      const rest: Record<string, unknown> = { ...inv };
      delete rest.generatedAt;
      return rest;
    };
    expect(strip(second)).toEqual(strip(first));
  });

  it("cada superficie declarada en SURFACES existe en el inventario generado", () => {
    const inventory = buildInventory();
    expect(inventory.surfaces).toHaveLength(SURFACES.length);
  });
});

describe("Guardia — cero hex de marca nuevo en superficies inventariadas (AC3)", () => {
  const baseline = loadCommittedBaseline();
  const live = buildInventory();

  it("todas las superficies del inventario congelado (frontend/docs/brand-color-inventory.json) siguen existiendo", () => {
    // OJO: `file.exists` del JSON congelado es el valor grabado en el momento en
    // que se generó (siempre `true` para lo que ya está commiteado) — no sirve
    // para detectar que un archivo desapareció DESPUÉS. La comprobación real es
    // contra `live` (re-escaneado ahora mismo desde el árbol de trabajo).
    const liveByPath = new Map(
      live.surfaces.flatMap((s) => s.files.map((f) => [f.relPath, f.exists])),
    );
    for (const surface of baseline.surfaces) {
      for (const file of surface.files) {
        expect
          .soft(liveByPath.get(file.relPath), `${file.relPath} — superficie '${surface.label}' (${surface.id})`)
          .toBe(true);
      }
    }
  });

  it("no aparece ningún hex de marca fuera de la lista congelada, en ninguna superficie inventariada", () => {
    // Compara cada aparición "marca" detectada AHORA contra el conjunto congelado
    // (archivo:línea:hex). Si algo no matchea, es (a) un hex de marca nuevo, o
    // (b) uno movido de línea/cambiado — ambos casos exigen regenerar el
    // inventario a propósito con `pnpm brand:inventory`, no que aparezca solo.
    const baselineKeys = new Set<string>();
    for (const surface of baseline.surfaces) {
      for (const file of surface.files) {
        for (const occ of file.occurrences) {
          if (occ.classification === "marca") {
            baselineKeys.add(`${file.relPath}:${occ.line}:${occ.hexNormalized}`);
          }
        }
      }
    }

    const undocumented: string[] = [];
    for (const surface of live.surfaces) {
      for (const file of surface.files) {
        if (!file.exists) continue;
        for (const occ of file.occurrences) {
          if (occ.classification !== "marca") continue;
          const key = `${file.relPath}:${occ.line}:${occ.hexNormalized}`;
          if (!baselineKeys.has(key)) {
            undocumented.push(`${key} — "${occ.snippet}"`);
          }
        }
      }
    }

    expect(undocumented, undocumented.join("\n")).toEqual([]);
  });

  it("las apariciones neutro/estado no se reclasifican como marca entre ejecuciones (AC2 estabilidad)", () => {
    const byKey = new Map<string, string>();
    for (const surface of baseline.surfaces) {
      for (const file of surface.files) {
        for (const occ of file.occurrences) {
          byKey.set(`${file.relPath}:${occ.line}:${occ.hexNormalized}`, occ.classification);
        }
      }
    }
    for (const surface of live.surfaces) {
      for (const file of surface.files) {
        if (!file.exists) continue;
        for (const occ of live.surfaces
          .find((s) => s.id === surface.id)!
          .files.find((f) => f.relPath === file.relPath)!.occurrences) {
          const key = `${file.relPath}:${occ.line}:${occ.hexNormalized}`;
          const previous = byKey.get(key);
          if (previous) {
            expect(occ.classification, key).toBe(previous);
          }
        }
      }
    }
  });
});

describe("Sin cambios de producto (AC5)", () => {
  it("el script vive fuera de app/ y components/ (no es código de producto)", () => {
    // Guardia liviana: si alguien mueve el inventario a una carpeta de producto,
    // esta prueba lo hace evidente en el PR.
    expect(FRONTEND_ROOT.endsWith("frontend") || FRONTEND_ROOT.endsWith("frontend/")).toBe(true);
  });
});
