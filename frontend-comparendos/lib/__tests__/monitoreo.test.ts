import { describe, expect, it } from "vitest";
import { resumenPlacas } from "@/lib/monitoreo";

describe("resumenPlacas", () => {
  it("concuerda en singular y avisa las repetidas del mismo pegado (B3-1)", () => {
    expect(resumenPlacas({ agregadas: [{}], yaExistian: ["TST123"], invalidas: ["AB12"], repetidas: ["NUE321"] })).toBe(
      "1 agregada · 1 ya estaba (TST123) · 1 repetida en lo que pegaste (NUE321) · 1 no es placa válida (AB12)",
    );
  });

  it("plural y sin secciones vacías", () => {
    expect(resumenPlacas({ agregadas: [{}, {}, {}], yaExistian: [], invalidas: [], repetidas: [] })).toBe("3 agregadas");
  });
});
