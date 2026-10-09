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

  it("OCR exige el permiso de ver paquetes y el prompt es solo del SuperAdmin", () => {
    const nav = comparendosNav({ ...SIN_CAPACIDADES, ocr: true });
    expect(nav.items.find((i) => i.key === "ocr")).toMatchObject({ href: "/ocr", permission: "comparendos.ocr.ver" });
    const prompt = nav.items.find((i) => i.key === "flit")?.children?.find((c) => c.key === "adm-ocr");
    expect(prompt).toMatchObject({ href: "/admin/ocr/prompts", superAdminOnly: true });
  });

  it("los envíos masivos de DP dependen de la capacidad y del permiso", () => {
    expect(comparendosNav(SIN_CAPACIDADES).items.map((i) => i.key)).not.toContain("dp-lotes");
    const lotes = comparendosNav({ ...SIN_CAPACIDADES, derechosPeticion: true }).items.find((i) => i.key === "dp-lotes");
    expect(lotes).toMatchObject({ href: "/dp/lotes", permission: "comparendos.dp.enviar_masivo" });
  });

  it("la administración FLIT es solo del SuperAdmin", () => {
    const flit = comparendosNav(SIN_CAPACIDADES).items.find((i) => i.key === "flit");
    expect(flit?.superAdminOnly).toBe(true);
    expect(flit?.children?.every((c) => c.superAdminOnly)).toBe(true);
  });
});
