import { describe, expect, it } from "vitest";
import { destinoDePendiente, enmascararCelular, enmascararDocumento, pasoTieneSecciones, textoPendiente } from "@/lib/tramite/resumen";
import { definicionCompleta } from "./fixtures-envio";
import { definicionDe } from "./fixtures-wizard";

describe("máscaras del resumen", () => {
  it("el documento deja el tipo y los últimos cuatro dígitos", () => {
    expect(enmascararDocumento("CC", "1020456789")).toBe("CC ••••6789");
    expect(enmascararDocumento(null, "1234567")).toBe("••••4567");
    expect(enmascararDocumento("CE", "123")).toBe("CE ••••");
    expect(enmascararDocumento("CC", null)).toBe("—");
  });

  it("el celular deja solo los dos últimos dígitos y nunca el número completo", () => {
    expect(enmascararCelular("3001234567")).toBe("••••••••67");
    expect(enmascararCelular("3001234567")).not.toContain("300");
    expect(enmascararCelular("12")).toBe("••••");
    expect(enmascararCelular("")).toBe("—");
  });
});

describe("pendientes en lenguaje claro", () => {
  const d = definicionCompleta();

  it("cada código tiene su texto, con el nombre del rol, del documento o del campo", () => {
    expect(textoPendiente({ codigo: "actor_requerido", referencia: "comprador", paso: "p2" }, d)).toBe("Falta registrar a: Comprador.");
    expect(textoPendiente({ codigo: "documento_requerido", referencia: "paz_salvo", paso: "p3" }, d)).toBe("Falta cargar el documento: Paz y salvo de impuestos.");
    expect(textoPendiente({ codigo: "campo_requerido", referencia: "cilindraje", paso: "p3" }, d)).toBe("Falta completar el campo: Cilindraje.");
    expect(textoPendiente({ codigo: "comercial_requerido", referencia: null, paso: "p3" }, d)).toBe("Falta el valor comercial.");
    expect(textoPendiente({ codigo: "prenda_acreedor_requerido", referencia: null, paso: null }, d)).toBe("Falta el acreedor de la prenda.");
    expect(textoPendiente({ codigo: "algo_nuevo", referencia: null, paso: null }, d)).toBe("Falta información por completar.");
  });

  it("el requisito de cobro que aún pide el back se dice sin hablar de pagos y no tiene paso al que ir", () => {
    const p = { codigo: "comprobante_pago_requerido", referencia: null, paso: null };

    expect(textoPendiente(p, d)).toBe("El servicio pide un requisito que este portal todavía no puede completar.");
    expect(destinoDePendiente(p, d)).toBeNull();
  });

  it("lleva al paso donde se corrige, y a ninguno si el tipo no tiene ese paso", () => {
    expect(destinoDePendiente({ codigo: "actor_requerido", referencia: "comprador", paso: "p2" }, d)).toBe("actores");
    expect(destinoDePendiente({ codigo: "documento_requerido", referencia: "paz_salvo", paso: "p3" }, d)).toBe("requisitos");
    const sinActores = definicionDe([["vehicle_query"], ["document_checklist"]]);

    expect(pasoTieneSecciones("actores", sinActores)).toBe(false);
    expect(destinoDePendiente({ codigo: "actor_requerido", referencia: "x", paso: null }, sinActores)).toBeNull();
  });
});
