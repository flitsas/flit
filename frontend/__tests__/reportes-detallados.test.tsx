import { describe, expect, it } from "vitest";
import { defaultDetailedFilters, toQueryParams } from "@/components/atom/modules/_reportesDetallados/filters";

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
