import { describe, expect, it } from "vitest";
import { nombreDescargaDeuda } from "@/lib/saneamiento/descargas";

// Uso de ejemplo: useDescarga(nombreDescargaDeuda(deuda.vehiculo.placa, "pdf")) → "deuda-TST001.pdf".
// N-3 / AP-3: el nombre por defecto (solo si falta Content-Disposition) es el mismo que arma el servidor
// (PdfDeuda.PlacaSegura): la placa en mayúsculas con solo [A-Z0-9]; si no queda nada, "sin-placa". Nunca la
// etiqueta visible «Sin placa» ni espacios.

describe("nombreDescargaDeuda (N-3 / AP-3)", () => {
  it("con placa: deuda-{PLACA}.pdf y .xlsx", () => {
    expect(nombreDescargaDeuda("TST001", "pdf")).toBe("deuda-TST001.pdf");
    expect(nombreDescargaDeuda("TST001", "xlsx")).toBe("deuda-TST001.xlsx");
  });

  it("reduce la placa a [A-Z0-9] en mayúsculas, como el servidor", () => {
    expect(nombreDescargaDeuda("tst 751", "xlsx")).toBe("deuda-TST751.xlsx");
    expect(nombreDescargaDeuda('ab"c;1\r\n', "pdf")).toBe("deuda-ABC1.pdf");
  });

  it.each([null, undefined, "", "   ", "  --  "])("sin placa utilizable (%j): deuda-sin-placa", (placa) => {
    expect(nombreDescargaDeuda(placa, "pdf")).toBe("deuda-sin-placa.pdf");
    expect(nombreDescargaDeuda(placa, "xlsx")).toBe("deuda-sin-placa.xlsx");
  });

  it("nunca contiene espacios", () => {
    expect(nombreDescargaDeuda(" a b ", "pdf")).not.toMatch(/\s/);
  });
});
