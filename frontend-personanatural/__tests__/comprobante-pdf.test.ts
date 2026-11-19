import { describe, expect, it } from "vitest";
import { armarPdf } from "@/lib/punto/comprobantePdf";

const texto = (bytes: Uint8Array) => Array.from(bytes, (b) => String.fromCharCode(b)).join("");

describe("armarPdf", () => {
  it("genera un PDF válido con las líneas, tildes escapadas en Latin-1 y paréntesis protegidos", () => {
    const pdf = texto(armarPdf(["FLIT - Comprobante del trámite", "Valor (COP): 366.000"]));
    expect(pdf.startsWith("%PDF-1.4")).toBe(true);
    expect(pdf.endsWith("%%EOF")).toBe(true);
    expect(pdf).toContain("(FLIT - Comprobante del trámite) Tj");
    expect(pdf).toContain(String.raw`(Valor \(COP\): 366.000) Tj`);
    expect(pdf).toContain("/Count 1");
  });

  it("pagina cuando hay muchas líneas", () => {
    const pdf = texto(armarPdf(Array.from({ length: 120 }, (_, i) => `Línea ${i}`)));
    expect(pdf).toContain("/Count 3");
  });
});
