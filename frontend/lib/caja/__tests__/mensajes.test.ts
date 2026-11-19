// Feature #14453 — un texto fijo por código del servidor; «ya pagado» y «total cambió» en amarillo, el resto en rojo.
import { describe, expect, it } from "vitest";
import { CajaError } from "@/lib/api/ot-caja";
import { ApiError } from "@/lib/api/types";
import { esperaLegible, mensajeDeCaja } from "../mensajes";

const e = (status: number, code: string, espera?: number) => new CajaError(status, "x", code, { code }, espera);

describe("mensajeDeCaja", () => {
  it.each([
    ["no_encontrado", 404, "No encontramos ese trámite.", "error"],
    ["otro_organismo", 403, "Este trámite no es de este organismo.", "error"],
    ["ya_pagado", 409, "Este trámite ya fue pagado.", "aviso"],
    ["cobro_vencido", 409, "El pago en caja de este trámite venció.", "error"],
    ["no_habilitado", 409, "Este trámite no tiene el pago en caja habilitado.", "error"],
    ["medio_caja_requerido", 400, "Elija cómo se cobró.", "error"],
    ["detalle_requerido", 400, "Escriba el detalle del medio.", "error"],
    ["prefactura_invalida", 400, "La prefactura debe ser PDF, JPG o PNG de hasta 5 MB.", "error"],
    ["total_cambio", 409, "El total cambió. Revise el nuevo valor y confirme de nuevo.", "aviso"],
    ["cobro_no_anulable", 409, "El trámite ya está en FLIT 2.0 y su cobro no se puede anular.", "error"],
  ])("%s → texto y tono", (code, status, texto, tono) => {
    expect(mensajeDeCaja(e(status, code))).toEqual({ texto, tono });
  });

  it("404 sin código es la caja apagada; 429 trae la espera", () => {
    expect(mensajeDeCaja(new ApiError(404, "x")).texto).toBe("La caja no está habilitada para tu organismo");
    expect(mensajeDeCaja(e(429, "rate_limited", 90)).texto).toBe("Demasiadas búsquedas sin resultado. Espere 2 minutos.");
  });

  it("un código desconocido o un error sin red cae en la frase genérica", () => {
    expect(mensajeDeCaja(e(500, "raro")).texto).toBe("No pudimos completar la acción. Intente de nuevo.");
    expect(mensajeDeCaja(new Error("boom")).tono).toBe("error");
  });

  it("espera legible", () => {
    expect(esperaLegible(30)).toBe("30 segundos");
    expect(esperaLegible(60)).toBe("1 minuto");
    expect(esperaLegible(undefined)).toBe("unos minutos");
  });
});
