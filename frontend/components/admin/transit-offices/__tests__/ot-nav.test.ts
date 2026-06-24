import { describe, expect, it } from "vitest";
import { foldOtSearch, matchesOtOfficeSearch } from "../ot-nav";

describe("ot-nav — HU #10236", () => {
  it("foldOtSearch ignora tildes", () => {
    expect(foldOtSearch("Bogotá")).toBe("bogota");
  });

  it("matchesOtOfficeSearch por nombre y codigo", () => {
    const office = { name: "Secretaría Bogotá", code: "11001" };
    expect(matchesOtOfficeSearch(office, "bogota")).toBe(true);
    expect(matchesOtOfficeSearch(office, "11001")).toBe(true);
    expect(matchesOtOfficeSearch(office, "medellin")).toBe(false);
  });
});
