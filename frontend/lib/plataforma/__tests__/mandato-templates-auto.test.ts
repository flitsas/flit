import { describe, it, expect } from "vitest";

import {
  MANDATO_TEMPLATE_AUTO,
  MANDATO_TEMPLATES,
  mandatoTemplateOptions,
} from "../mandato-templates";

// Uso de ejemplo:
// mandatoTemplateOptions() → [{ code: "auto", ... }, { code: "generico", ... }, ...]
// Alimenta el selector de plantilla por OT (HU #11705).

describe("mandatoTemplateOptions — opciones del selector por OT", () => {
  it("happy path: encabeza la automática y sigue con las redacciones del sistema", () => {
    const options = mandatoTemplateOptions();

    expect(options[0].code).toBe("auto");
    expect(options).toHaveLength(MANDATO_TEMPLATES.length + 1);
    expect(options.map((o) => o.code)).toEqual([
      "auto",
      ...MANDATO_TEMPLATES.map((t) => t.code),
    ]);
  });

  it("cada opción trae etiqueta y resumen para poder elegir sin adivinar", () => {
    for (const option of mandatoTemplateOptions()) {
      expect(option.label.trim().length).toBeGreaterThan(0);
      expect(option.summary.trim().length).toBeGreaterThan(0);
    }
  });

  it("edge case — la automática NO es una redacción: no está en el catálogo del generador", () => {
    // Si "auto" apareciera como redacción, el backend intentaría generar un PDF con ese código.
    expect(MANDATO_TEMPLATES.some((t) => (t.code as string) === MANDATO_TEMPLATE_AUTO.code)).toBe(
      false,
    );
  });
});
