import { describe, expect, it } from "vitest";
import { mensajeExportacion, nombreParte, partesDe } from "@/lib/exportar";

describe("exportación por partes (HU #13871)", () => {
  it("AC1/AC2: reparte en archivos de hasta 5.000 filas", () => {
    expect(partesDe(0)).toBe(1);
    expect(partesDe(3200)).toBe(1);
    expect(partesDe(5000)).toBe(1);
    expect(partesDe(7000)).toBe(2);
    expect(partesDe(30001)).toBe(7);
  });

  it("AC2: el nombre lleva la parte solo cuando hay más de un archivo", () => {
    expect(nombreParte("2026-10-23", 1, 1)).toBe("comparendos-2026-10-23.xlsx");
    expect(nombreParte("2026-10-23", 2, 2)).toBe("comparendos-2026-10-23-parte-2-de-2.xlsx");
  });

  it("AC2: dice cuántos, en cuántos archivos y con cuántas columnas", () => {
    expect(mensajeExportacion(7000, 2, 3)).toBe("Se exportaron 7.000 comparendos en 2 archivos de hasta 5.000 filas cada uno, con 3 columnas visibles.");
    expect(mensajeExportacion(1, 1, 1)).toBe("Se exportó 1 comparendo con 1 columna visible.");
  });
});
