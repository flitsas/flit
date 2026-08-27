import { describe, expect, it } from "vitest";
import {
  resolveAssignmentMode,
  resolveTipoNegocio,
  suggestedFamilyForTipo,
  systemTemplateLabel,
  tipoNegocioLabel,
  terceroAjenoEnPlantilla,
} from "@/lib/plataforma/mandato-templates";

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

  it("expone labels de producto", () => {
    expect(tipoNegocioLabel("persona_rl")).toMatch(/persona/i);
    expect(tipoNegocioLabel("institucional")).toMatch(/institucional/i);
    expect(tipoNegocioLabel("abierto")).toMatch(/abierto/i);
  });

  it("etiqueta la redacción del sistema para el badge", () => {
    expect(systemTemplateLabel("generico")).toBe("Genérico");
    expect(systemTemplateLabel("sabaneta")).toBe("Sabaneta");
    expect(systemTemplateLabel("bello")).toBe("Bello");
    expect(systemTemplateLabel("municipio")).toMatch(/Envigado.*Funza.*Medellín/i);
    expect(systemTemplateLabel(null)).toBe("Genérico");
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
