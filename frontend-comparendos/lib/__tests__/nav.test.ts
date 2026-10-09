import { describe, expect, it } from "vitest";
import { comparendosNav } from "@/lib/nav";
import { SIN_CAPACIDADES } from "@/lib/sesion";

describe("comparendosNav", () => {
  it("oculta OCR y reportes si la empresa no tiene esas capacidades", () => {
    const keys = comparendosNav(SIN_CAPACIDADES).items.map((i) => i.key);
    expect(keys).toContain("bandeja");
    expect(keys).not.toContain("ocr");
    expect(keys).not.toContain("dashboard");
  });

  it("muestra OCR y reportes cuando la empresa los tiene", () => {
    const keys = comparendosNav({ ...SIN_CAPACIDADES, ocr: true, reportes: true }).items.map((i) => i.key);
    expect(keys).toEqual(expect.arrayContaining(["ocr", "dashboard", "productividad"]));
  });

  it("la administración FLIT es solo del SuperAdmin", () => {
    const flit = comparendosNav(SIN_CAPACIDADES).items.find((i) => i.key === "flit");
    expect(flit?.superAdminOnly).toBe(true);
    expect(flit?.children?.every((c) => c.superAdminOnly)).toBe(true);
  });
});
