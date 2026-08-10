import { describe, expect, it } from "vitest";
import {
  resolveAssignmentMode,
  resolveTipoNegocio,
  suggestedFamilyForTipo,
  systemTemplateLabel,
  tipoNegocioLabel,
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
