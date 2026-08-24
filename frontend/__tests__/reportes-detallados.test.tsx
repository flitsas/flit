import { describe, expect, it } from "vitest";
import type { ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";
import {
  defaultDetailedFilters,
  familyToCategory,
  groupProcedureTypes,
  toQueryParams,
} from "@/components/atom/modules/_reportesDetallados/filters";

describe("detailed report filters", () => {
  it("maps tri-state flags to booleans", () => {
    const filters = {
      ...defaultDetailedFilters(),
      procedureTypeId: "abc",
      category: "traspasos",
      status: "aprobado",
      hasTransformation: "true" as const,
      isLeasing: "false" as const,
    };
    const params = toQueryParams(filters, 2, 50);
    expect(params.hasTransformation).toBe(true);
    expect(params.isLeasing).toBe(false);
    expect(params.page).toBe(2);
    expect(params.pageSize).toBe(50);
    expect(params.procedureTypeId).toBe("abc");
  });

  it("no adelanta el rango un día por la tarde", () => {
    // 9 de la noche del 10 de agosto. Con `toISOString()` esto caía en el 11 en UTC, así que el
    // filtro abría diciendo «hasta mañana» y corría el inicio el mismo día. La hora se construye
    // con los componentes locales para que la prueba diga lo mismo en cualquier máquina.
    const nocheDelDiez = new Date(2026, 7, 10, 21, 0, 0);

    const { range } = defaultDetailedFilters(nocheDelDiez);

    expect(range.to).toBe("2026-08-10");
    expect(range.from).toBe("2026-07-11");
  });
});

describe("groupProcedureTypes", () => {
  const types: ProcedureTypeSummary[] = [
    { id: "1", code: "TRA", name: "Traspaso simple", family: "TRASPASO", publicationStatus: "published", isActive: true, wizardEnabled: true, publishedAt: null },
    { id: "2", code: "MAT", name: "Matrícula inicial", family: "MATRICULAS", publicationStatus: "published", isActive: true, wizardEnabled: true, publishedAt: null },
    { id: "3", code: "OTR", name: "Otro trámite", family: "OTROS", publicationStatus: "published", isActive: true, wizardEnabled: true, publishedAt: null },
  ];

  it("mapea familia a la categoría de la vista BI", () => {
    expect(familyToCategory("MATRICULAS")).toBe("matriculas");
    expect(familyToCategory("TRASPASO")).toBe("traspasos");
    expect(familyToCategory("OTROS")).toBe("otros");
  });

  it("agrupa por categoría en el orden canónico", () => {
    const groups = groupProcedureTypes(types);
    expect(groups.map((g) => g.category)).toEqual(["matriculas", "traspasos", "otros"]);
    expect(groups[0].types[0].name).toBe("Matrícula inicial");
  });
});
