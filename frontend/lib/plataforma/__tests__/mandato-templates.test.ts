import { describe, expect, it } from "vitest";
import type { MandatoFormatView } from "@/lib/api/admin-plataforma-mandatos";
import {
  MANDATO_TIPOS,
  assignmentModeFromFormat,
  mandatoFormatName,
  resumenTiposPorCompania,
  resolveAssignmentMode,
  resolveTipoNegocio,
  suggestedFamilyForTipo,
  tipoNegocioLabel,
  terceroAjenoEnPlantilla,
} from "@/lib/plataforma/mandato-templates";

const fmt = (code: string, name: string, assignmentMode = "signer"): MandatoFormatView => ({
  code,
  name,
  assignmentMode,
  baseRedaction: code === "auto" ? null : code,
  selectableAsRedaction: code !== "auto",
  delegatesToOfficeTemplate: code === "auto", currentVersion: 0, hasCustomTemplate: false, rowVersion: 1, updatedAt: null,
});
const FORMATOS: MandatoFormatView[] = [
  fmt("auto", "Automática"),
  fmt("generico", "Genérico"),
  fmt("sabaneta", "Sabaneta", "institutional"),
  fmt("bello", "Bello"),
  fmt("municipio", "Envigado, Funza y Medellín"),
];

describe("mandato-templates tipos de negocio", () => {
  it("mapea assignment_mode → tipo UI", () => {
    expect(resolveTipoNegocio("signer")).toBe("persona_rl");
    expect(resolveTipoNegocio("institutional")).toBe("institucional");
    expect(resolveTipoNegocio("open")).toBe("abierto");
    expect(resolveTipoNegocio(undefined)).toBe("persona_rl");
  });

  it("mapea tipo UI → assignment_mode sin inventar plantillas", () => {
    expect(resolveAssignmentMode("persona_rl")).toBe("signer");
    expect(resolveAssignmentMode("institucional")).toBe("institutional");
    expect(resolveAssignmentMode("abierto")).toBe("open");
  });

  it("sugiere familia sin forzar redacción", () => {
    expect(suggestedFamilyForTipo("institucional", "generico")).toBe("organismo_transito");
    expect(suggestedFamilyForTipo("persona_rl", "generico")).toBe("individuo");
    expect(suggestedFamilyForTipo("persona_rl", "bello")).toBe("organismo_transito");
    expect(suggestedFamilyForTipo("abierto", "sabaneta")).toBe("individuo");
  });

  it("el modo persistido sale del tipo por defecto del formato del catálogo", () => {
    expect(assignmentModeFromFormat(FORMATOS, "sabaneta")).toBe("institutional");
    expect(assignmentModeFromFormat(FORMATOS, "generico")).toBe("signer");
    expect(assignmentModeFromFormat(FORMATOS, "bello")).toBe("signer");
    expect(assignmentModeFromFormat(FORMATOS, "auto")).toBe("signer");
    // Un código que ya no está en el catálogo cae en Persona natural.
    expect(assignmentModeFromFormat(FORMATOS, "retirado")).toBe("signer");
    // Si el backend cambia el tipo de un formato, se sigue el catálogo y no una constante.
    expect(assignmentModeFromFormat([fmt("bello", "Bello", "institutional")], "bello")).toBe(
      "institutional",
    );
  });

  it("expone labels de producto", () => {
    expect(MANDATO_TIPOS.map((t) => t.label).join(" ")).not.toMatch(/persona o rl|institucional|sin asumir/i);
    expect(MANDATO_TIPOS.find((t) => t.value === "abierto")?.summary).not.toMatch(/default de modo/i);
    expect(tipoNegocioLabel("persona_rl")).toBe("Persona natural");
    expect(tipoNegocioLabel("institucional")).toBe("Persona jurídica");
    expect(tipoNegocioLabel("abierto")).toBe("Mandato abierto");
  });

  it("muestra el nombre vigente del catálogo y, si el código ya no existe, el código guardado", () => {
    expect(mandatoFormatName(FORMATOS, "generico")).toBe("Genérico");
    expect(mandatoFormatName(FORMATOS, "municipio")).toMatch(/Envigado.*Funza.*Medellín/i);
    expect(mandatoFormatName([fmt("bello", "Bello Renombrado")], "bello")).toBe("Bello Renombrado");
    expect(mandatoFormatName(FORMATOS, "retirado")).toBe("retirado");
    expect(mandatoFormatName(FORMATOS, null)).toBe("");
  });

  it("resume los tipos por compañía: Persona natural siempre, y las excepciones con su cantidad", () => {
    expect(resumenTiposPorCompania(0, 0)).toBe("Persona natural");
    expect(resumenTiposPorCompania(2, 1)).toBe("Persona natural · Persona jurídica (2) · Mandato abierto (1)");
  });
});

describe("terceroAjenoEnPlantilla (HU #11718)", () => {
  it("la automática nunca advierte", () => {
    expect(terceroAjenoEnPlantilla("auto", "11001000")).toBeNull();
    expect(terceroAjenoEnPlantilla(null, "11001000")).toBeNull();
  });

  it("la genérica no nombra a ningún organismo concreto", () => {
    expect(terceroAjenoEnPlantilla("generico", "11001000")).toBeNull();
  });

  it("la redacción propia del organismo no advierte", () => {
    expect(terceroAjenoEnPlantilla("sabaneta", "5631000")).toBeNull();
    expect(terceroAjenoEnPlantilla("municipio", "5266000")).toBeNull();
    expect(terceroAjenoEnPlantilla("municipio", "25286000")).toBeNull();
  });

  it("una redacción de otro organismo advierte y nombra al tercero", () => {
    // Es el caso que se vio en vivo: Bello aplicado a Bogotá cierra «en el municipio de Bello».
    expect(terceroAjenoEnPlantilla("bello", "11001000")).toContain("BELLO");
    expect(terceroAjenoEnPlantilla("sabaneta", "25286000")).toContain("SABANETA");
  });

  it("municipio advierte fuera de Funza y Medellín", () => {
    expect(terceroAjenoEnPlantilla("municipio", "11001000")).not.toBeNull();
    expect(terceroAjenoEnPlantilla("sabaneta", "5266000")).toContain("SABANETA");
  });
});
