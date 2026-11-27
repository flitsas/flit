import { describe, expect, it } from "vitest";
import {
  DIAS_POR_DEFECTO,
  MENSAJE_RANGO_GENERICO,
  MENSAJE_RANGO_MAXIMO,
  RANGO_MAXIMO_DIAS,
  diasDelRango,
  hoyColombia,
  leerRango,
  mensajeRangoInvalido,
  rangoPorDefecto,
  validarRango,
} from "@/lib/estadisticas/periodo";

// Uso de ejemplo: rangoPorDefecto(hoyColombia()) → { desde: hoy − 29 días, hasta: hoy } (D-DASH: últimos 30 días);
// validarRango({ desde, hasta }, hoy) → null o el error del campo; el contrato responde 400 RANGO_INVALIDO con un rango
// invertido o de más de 366 días (impuestos.v1.yaml, parámetros Desde/Hasta).

describe("periodo del dashboard y de los reportes", () => {
  it("hoyColombia usa la fecha de Bogotá (UTC−5), no la del servidor ni la UTC", () => {
    // 2026-11-25 03:00 UTC = 2026-11-24 22:00 en Bogotá.
    expect(hoyColombia(new Date("2026-11-25T03:00:00Z"))).toBe("2026-11-24");
    expect(hoyColombia(new Date("2026-11-25T05:00:00Z"))).toBe("2026-11-25");
  });

  it("por defecto son los últimos 30 días con hoy incluido", () => {
    expect(DIAS_POR_DEFECTO).toBe(30);
    const rango = rangoPorDefecto("2026-11-24");
    expect(rango).toEqual({ desde: "2026-10-26", hasta: "2026-11-24" });
    expect(diasDelRango(rango)).toBe(30);
  });

  it("acepta un rango de exactamente 366 días y rechaza el de 367 con el literal de la spec", () => {
    expect(RANGO_MAXIMO_DIAS).toBe(366);
    expect(validarRango({ desde: "2025-11-24", hasta: "2026-11-24" }, "2026-11-24")).toBeNull();
    expect(validarRango({ desde: "2025-11-23", hasta: "2026-11-24" }, "2026-11-24")).toEqual({ campo: "hasta", mensaje: MENSAJE_RANGO_MAXIMO });
    expect(MENSAJE_RANGO_MAXIMO).toBe("El rango no puede superar un año");
  });

  it("rechaza «Hasta» en el futuro, «Hasta» anterior a «Desde» y fechas que no existen", () => {
    expect(validarRango({ desde: "2026-11-01", hasta: "2026-11-25" }, "2026-11-24")?.campo).toBe("hasta");
    expect(validarRango({ desde: "2026-11-10", hasta: "2026-11-01" }, "2026-11-24")?.campo).toBe("hasta");
    expect(validarRango({ desde: "2026-02-30", hasta: "2026-11-01" }, "2026-11-24")?.campo).toBe("desde");
    expect(validarRango({ desde: "", hasta: "2026-11-01" }, "2026-11-24")?.campo).toBe("desde");
  });

  it("leerRango toma ?desde&hasta de la URL si son válidos y si no vuelve al de por defecto", () => {
    const hoy = "2026-11-24";
    expect(leerRango(new URLSearchParams("desde=2026-11-01&hasta=2026-11-10"), hoy)).toEqual({ desde: "2026-11-01", hasta: "2026-11-10" });
    expect(leerRango(new URLSearchParams("desde=2020-01-01&hasta=2026-11-10"), hoy)).toEqual(rangoPorDefecto(hoy));
    expect(leerRango(new URLSearchParams(""), hoy)).toEqual(rangoPorDefecto(hoy));
  });

  it("un 400 RANGO_INVALIDO se explica con la causa que vea el cliente o con el texto genérico", () => {
    expect(mensajeRangoInvalido({ desde: "2024-01-01", hasta: "2026-11-24" }, "2026-11-24")).toBe(MENSAJE_RANGO_MAXIMO);
    expect(mensajeRangoInvalido({ desde: "2026-11-01", hasta: "2026-11-10" }, "2026-11-24")).toBe(MENSAJE_RANGO_GENERICO);
  });
});
