import { describe, expect, it } from "vitest";
import { ApiError } from "@/lib/api/errores";
import { conservaLlave, esEstadoFinal, INTERVALO_SONDEO_MS, pasosDeSeguimiento } from "@/lib/tramite/envio";
import { definicionCompleta } from "./fixtures-envio";
import { definicionDe } from "./fixtures-wizard";

const MARCA = "2026-10-28T15:30:00Z";

describe("llave de idempotencia entre intentos", () => {
  it("se reutiliza si no se sabe qué pasó: sin red, 5xx y 429", () => {
    expect(conservaLlave(new TypeError("fetch failed"))).toBe(true);
    expect(conservaLlave(new ApiError("x", "t", 503))).toBe(true);
    expect(conservaLlave(new ApiError("x", "t", 500))).toBe(true);
    expect(conservaLlave(new ApiError("rate_limited", "t", 429))).toBe(true);
  });

  it("se renueva ante un rechazo definitivo del back (422, 409, 400)", () => {
    expect(conservaLlave(new ApiError("requisitos_incompletos", "t", 422))).toBe(false);
    expect(conservaLlave(new ApiError("identity_pending", "t", 409))).toBe(false);
    expect(conservaLlave(new ApiError("idempotency_conflicto", "t", 409))).toBe(false);
    expect(conservaLlave(new ApiError("idempotency_key_requerida", "t", 400))).toBe(false);
  });
});

describe("sondeo y estados finales", () => {
  it("el intervalo es prudente (entre 10 y 15 s) y solo se detiene en estados finales", () => {
    expect(INTERVALO_SONDEO_MS).toBeGreaterThanOrEqual(10_000);
    expect(INTERVALO_SONDEO_MS).toBeLessThanOrEqual(15_000);
    expect(["creada", "rechazada_creacion", "cancelada"].every(esEstadoFinal)).toBe(true);
    expect(["en_cola", "borrador", "pendiente_validacion"].some(esEstadoFinal)).toBe(false);
  });
});

describe("pasos del seguimiento", () => {
  const d = definicionCompleta();

  it("en cola: enviado con su fecha y la creación en curso", () => {
    expect(pasosDeSeguimiento({ estado: "en_cola", actualizadoEn: MARCA }, d)).toEqual([
      { titulo: "Borrador del trámite", estado: "done", fecha: null },
      { titulo: "Validación de identidad", estado: "done", fecha: null },
      { titulo: "Enviado a FLIT 2", estado: "done", fecha: "28/10/2026 10:30" },
      { titulo: "Creado en FLIT 2", estado: "current", fecha: null },
    ]);
  });

  it("creada: la fecha del último cambio es la de la creación", () => {
    const p = pasosDeSeguimiento({ estado: "creada", actualizadoEn: MARCA }, d);

    expect(p[3]).toEqual({ titulo: "Creado en FLIT 2", estado: "done", fecha: "28/10/2026 10:30" });
    expect(p[2].fecha).toBeNull();
  });

  it("rechazada la creación: el último paso queda rechazado", () => {
    expect(pasosDeSeguimiento({ estado: "rechazada_creacion", actualizadoEn: MARCA }, d)[3].estado).toBe("rejected");
  });

  it("si el tipo no exige validar la identidad, ese paso se muestra omitido", () => {
    const sinIdentidad = definicionDe([["vehicle_query"], ["actor_form"]]);

    expect(pasosDeSeguimiento({ estado: "en_cola", actualizadoEn: MARCA }, sinIdentidad)[1].estado).toBe("skipped");
  });
});
