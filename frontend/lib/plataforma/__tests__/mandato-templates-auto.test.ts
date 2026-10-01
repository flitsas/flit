import { describe, it, expect } from "vitest";
import type { MandatoFormatView } from "@/lib/api/admin-plataforma-mandatos";

import * as templates from "../mandato-templates";
import { MANDATO_TEMPLATE_AUTO_CODE, findMandatoFormat } from "../mandato-templates";

// HU #13174 — las opciones del selector salen del catálogo del backend, no de constantes del frontend.

const FORMATOS: MandatoFormatView[] = [
  {
    code: "auto",
    name: "Automática (según el organismo)",
    assignmentMode: "signer",
    baseRedaction: null,
    selectableAsRedaction: false,
    delegatesToOfficeTemplate: true, currentVersion: 0, hasCustomTemplate: false, rowVersion: 1, updatedAt: null,
  },
  {
    code: "generico",
    name: "Genérico renombrado",
    assignmentMode: "signer",
    baseRedaction: "generico",
    selectableAsRedaction: true,
    delegatesToOfficeTemplate: false, currentVersion: 0, hasCustomTemplate: false, rowVersion: 1, updatedAt: null,
  },
];

describe("catálogo de formatos en mandato-templates", () => {
  it("encuentra el formato por código sin distinguir mayúsculas y devuelve el nombre del backend", () => {
    expect(findMandatoFormat(FORMATOS, "GENERICO")?.name).toBe("Genérico renombrado");
    expect(findMandatoFormat(FORMATOS, "retirado")).toBeUndefined();
    expect(findMandatoFormat(FORMATOS, null)).toBeUndefined();
  });

  it("la automática no es una redacción: el catálogo la marca como delegación", () => {
    const auto = findMandatoFormat(FORMATOS, MANDATO_TEMPLATE_AUTO_CODE);
    expect(auto?.selectableAsRedaction).toBe(false);
    expect(auto?.baseRedaction).toBeNull();
  });

  it("el módulo ya no define la lista ni el tipo local de formatos", () => {
    const exported = Object.keys(templates);
    expect(exported).not.toContain("MANDATO_TEMPLATES");
    expect(exported).not.toContain("mandatoTemplateOptions");
    expect(exported).not.toContain("systemTemplateLabel");
  });
});
