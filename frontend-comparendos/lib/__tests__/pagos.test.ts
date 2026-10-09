import { describe, expect, it } from "vitest";
import { armarPago, diferencia, errorArchivoCarga, erroresPago, leerDatosFila, leerValor, type FormPago } from "@/lib/pagos";

const F: FormPago = { tipo: "BC", valor: "$ 302.050", fecha: "2026-10-08", comprobante: null };

describe("registro de un pago", () => {
  it("lee el valor sin importar los separadores", () => {
    expect(leerValor("$ 302.050")).toBe(302050);
    expect(leerValor("604100")).toBe(604100);
    expect(leerValor("abc")).toBeNull();
  });

  it("listo cuando tiene tipo, valor y fecha no futura", () => {
    expect(erroresPago(F, "2026-10-09")).toEqual([]);
  });

  it("rechaza lo que la API rechazaría", () => {
    expect(erroresPago({ tipo: "", valor: "0", fecha: "", comprobante: null }, "2026-10-09")).toEqual([
      "Elige el tipo de pago.",
      "Escribe el valor pagado.",
      "Escribe la fecha de pago.",
    ]);
    expect(erroresPago({ ...F, fecha: "2026-10-10" }, "2026-10-09")).toEqual(["La fecha de pago no puede ser futura."]);
  });

  it("el comprobante es PDF, JPG o PNG de hasta 10 MB", () => {
    const word = new File(["x"], "pago.docx", { type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document" });
    expect(erroresPago({ ...F, comprobante: word }, "2026-10-09")).toEqual(["El comprobante debe ser PDF, JPG o PNG."]);
    const grande = new File([new Uint8Array(10 * 1024 * 1024 + 1)], "pago.pdf", { type: "application/pdf" });
    expect(erroresPago({ ...F, comprobante: grande }, "2026-10-09")).toEqual(["El comprobante supera 10 MB."]);
  });

  it("arma el multipart con el valor en número", () => {
    const form = armarPago(F);
    expect(form.get("tipo")).toBe("BC");
    expect(form.get("valor")).toBe("302050");
    expect(form.get("fecha")).toBe("2026-10-08");
    expect(form.get("comprobante")).toBeNull();
  });

  it("calcula la diferencia con el valor del comparendo", () => {
    expect(diferencia(302050, 604100)).toBe(-302050);
    expect(diferencia(604100, null)).toBeNull();
  });
});

describe("carga masiva de pagos", () => {
  it("solo acepta .xlsx", () => {
    expect(errorArchivoCarga(new File(["x"], "pagos.XLSX"))).toBeNull();
    expect(errorArchivoCarga(new File(["x"], "pagos.csv"))).toBe("Carga un archivo de Excel (.xlsx) con la plantilla de pagos.");
  });

  it("lee los datos de la fila aunque falten campos o el JSON esté dañado", () => {
    expect(leerDatosFila('{"tipoPago":"PC","valor":1000,"fechaPago":"2026-10-08"}')).toEqual({
      tipoPago: "PC",
      placa: null,
      numeroComparendo: null,
      valor: 1000,
      fechaPago: "2026-10-08",
    });
    expect(leerDatosFila("no es json").tipoPago).toBeNull();
  });
});
