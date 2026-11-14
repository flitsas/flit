import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { ENLACE_ACCION_CLS, FOCO_CLS } from "@/components/configuracion/estilos";

// Uso de ejemplo (UA-12, hueco H-10 de la auditoría UI de HU #14577):
//   import { ENLACE_ACCION_CLS } from "@/components/configuracion/estilos";
//   <button className={ENLACE_ACCION_CLS}>Cargar evidencia</button>
// Una sola clase para el enlace de acción de celda, con el alto mínimo de 36 px de las barras densas (H-6).

const RAIZ = join(__dirname, "..", "..");
const ARCHIVOS = [
  "saneamiento/PanelDeudaSaneamiento.tsx",
  "vehiculos/ListadoVehiculos.tsx",
  "backoffice/RenglonesGestion.tsx",
  "backoffice/BandejaSaneamientos.tsx",
  "tablero/BotonCrearTramite.tsx",
];

describe("ENLACE_ACCION_CLS — enlace de acción compartido (UA-12)", () => {
  it("vive en estilos.ts con inline-flex, min-h-9, la tinta de marca, subrayado y el foco del kit", () => {
    const clases = ENLACE_ACCION_CLS.split(/\s+/);
    for (const c of ["inline-flex", "min-h-9", "items-center", "text-xs", "font-semibold", "underline", "text-[var(--flit-brand-ink)]"]) expect(clases).toContain(c);
    expect(ENLACE_ACCION_CLS).toContain(FOCO_CLS);
  });

  it("los cinco archivos lo usan y ninguno define su propio ENLACE_CLS", () => {
    for (const archivo of ARCHIVOS) {
      const fuente = readFileSync(join(RAIZ, archivo), "utf8");
      expect(fuente, archivo).not.toMatch(/const ENLACE_CLS\s*=/);
      expect(fuente, archivo).toContain("ENLACE_ACCION_CLS");
    }
  });
});
