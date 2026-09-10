import { describe, expect, it } from "vitest";
import { resolveActorRole, resolveStepBody } from "@/components/operacion/sectionRendererRegistry";
import type { WizardSectionType } from "@/lib/api/types/procedure-runtime";

/**
 * ADR-0050 / CFD-09 — el cuerpo del paso se decide por el `section_type` parametrizado del tipo, no
 * por la clave del paso. Es lo que permite que un trámite de la familia OTROS se dibuje sin que el
 * cliente conozca sus claves.
 */
describe("SectionRendererRegistry", () => {
  it("resuelve el cuerpo por section_type, ignorando la clave", () => {
    // La clave 'propietario' no existía en el switch heredado: antes no pintaba nada.
    expect(resolveStepBody({ key: "propietario", sectionType: "actor_form" })).toBe("actores");
    expect(resolveStepBody({ key: "loquesea", sectionType: "vehicle_query" })).toBe("consulta");
    expect(resolveStepBody({ key: "otra", sectionType: "signature_fur" })).toBe("fur");
  });

  it("cae a la clave heredada cuando el paso no trae section_type", () => {
    // Expedientes cuyo estado del asistente es anterior a la parametrización.
    expect(resolveStepBody({ key: "consulta_vin" })).toBe("consulta");
    expect(resolveStepBody({ key: "vendedor" })).toBe("actores");
    expect(resolveStepBody({ key: "identidad" })).toBe("identidad");
  });

  it("normaliza la clave 'comercial' a Requisitos", () => {
    // Los datos comerciales dejaron de tener paso propio; un borrador viejo puede seguir ahí.
    expect(resolveStepBody({ key: "comercial" })).toBe("documentos");
  });

  it("una clave desconocida sin section_type cae al cuerpo genérico, no rompe", () => {
    expect(resolveStepBody({ key: "inventada" })).toBe("generico");
  });

  it("cubre los nueve section_type del catálogo cerrado", () => {
    const todos: WizardSectionType[] = [
      "vehicle_query",
      "document_checklist",
      "actor_form",
      "commercial",
      "biometric",
      "signature_fur",
      "plate_request",
      "prenda_decision",
      "generic_form",
    ];

    // Ninguno debe caer al genérico por descuido salvo los que no tienen cuerpo propio.
    const sinCuerpoPropio = new Set<WizardSectionType>(["plate_request", "generic_form"]);
    for (const sectionType of todos) {
      const cuerpo = resolveStepBody({ key: "x", sectionType });
      if (sinCuerpoPropio.has(sectionType)) {
        expect(cuerpo).toBe("generico");
      } else {
        expect(cuerpo).not.toBe("generico");
      }
    }
  });

  it("el paso de actores sabe a qué rol corresponde", () => {
    expect(resolveActorRole({ key: "vendedor" })).toBe("vendedor");
    expect(resolveActorRole({ key: "comprador" })).toBe("comprador");
    // En OTROS el titular se persiste como comprador aunque el paso se titule "Propietario".
    expect(resolveActorRole({ key: "propietario" })).toBe("comprador");
  });
});
