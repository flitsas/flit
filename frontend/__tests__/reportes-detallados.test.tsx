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
});

describe("groupProcedureTypes", () => {
  const types: ProcedureTypeSummary[] = [
    { id: "1", code: "TRA", name: "Traspaso simple", family: "TRASPASO", publicationStatus: "published", isActive: true, publishedAt: null },
    { id: "2", code: "MAT", name: "Matrícula inicial", family: "MATRICULAS", publicationStatus: "published", isActive: true, publishedAt: null },
    { id: "3", code: "OTR", name: "Otro trámite", family: "OTROS", publicationStatus: "published", isActive: true, publishedAt: null },
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
