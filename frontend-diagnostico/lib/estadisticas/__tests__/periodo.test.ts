import { describe, expect, it } from "vitest";
import {
  MENSAJE_HASTA_ANTERIOR,
  MENSAJE_HASTA_FUTURO,
  MENSAJE_RANGO_GENERICO,
  MENSAJE_RANGO_MAXIMO,
  diasDelRango,
  hoyColombia,
  leerRango,
  mensajeRangoInvalido,
  rangoPorDefecto,
  sumarDias,
  validarRango,
} from "@/lib/estadisticas/periodo";

// Uso de ejemplo: const hoy = hoyColombia(); const r = leerRango(searchParams, hoy); validarRango(r, hoy) → null o
// { campo: "hasta", mensaje: "El rango no puede superar un año" } antes de pedir GET /estadisticas.

describe("periodo — fechas en hora Colombia", () => {
  it("hoyColombia toma el día de Bogotá (UTC−5), no el de UTC", () => {
    expect(hoyColombia(new Date("2026-11-16T03:00:00Z"))).toBe("2026-11-15");
    expect(hoyColombia(new Date("2026-11-16T05:00:00Z"))).toBe("2026-11-16");
  });

  it("sumarDias cruza meses y años", () => {
    expect(sumarDias("2026-03-01", -1)).toBe("2026-02-28");
    expect(sumarDias("2026-12-31", 1)).toBe("2027-01-01");
  });

  it("el rango por defecto son los últimos 30 días con hoy incluido", () => {
    expect(rangoPorDefecto("2026-11-15")).toEqual({ desde: "2026-10-17", hasta: "2026-11-15" });
    expect(diasDelRango({ desde: "2026-10-17", hasta: "2026-11-15" })).toBe(30);
  });
});

describe("periodo — validarRango (contrato §0: desde ≤ hasta ≤ hoy, máximo 366 días)", () => {
  const hoy = "2026-11-15";

  it("acepta un rango de exactamente 366 días que termina hoy", () => {
    expect(validarRango({ desde: sumarDias(hoy, -365), hasta: hoy }, hoy)).toBeNull();
  });

  it("rechaza más de 366 días con el texto del límite en «Hasta»", () => {
    expect(validarRango({ desde: sumarDias(hoy, -366), hasta: hoy }, hoy)).toEqual({ campo: "hasta", mensaje: MENSAJE_RANGO_MAXIMO });
  });

  it("rechaza un «Hasta» en el futuro", () => {
    expect(validarRango({ desde: "2026-11-01", hasta: "2026-11-16" }, hoy)).toEqual({
      campo: "hasta",
      mensaje: "La fecha final no puede ser posterior a hoy",
    });
  });

  it("rechaza «Hasta» anterior a «Desde» y las fechas vacías o mal formadas", () => {
    expect(validarRango({ desde: "2026-11-10", hasta: "2026-11-01" }, hoy)?.campo).toBe("hasta");
    expect(validarRango({ desde: "", hasta: hoy }, hoy)?.campo).toBe("desde");
    expect(validarRango({ desde: "2026-13-40", hasta: hoy }, hoy)?.campo).toBe("desde");
  });
});

describe("periodo — leerRango de la URL", () => {
  it("usa desde y hasta de la URL si son válidos", () => {
    const p = new URLSearchParams("desde=2026-01-01&hasta=2026-01-31");
    expect(leerRango(p, "2026-11-15")).toEqual({ desde: "2026-01-01", hasta: "2026-01-31" });
  });

  it("cae al rango por defecto si faltan, están mal formadas o el rango es inválido", () => {
    expect(leerRango(new URLSearchParams(), "2026-11-15")).toEqual({ desde: "2026-10-17", hasta: "2026-11-15" });
    expect(leerRango(new URLSearchParams("desde=ayer&hasta=hoy"), "2026-11-15")).toEqual({ desde: "2026-10-17", hasta: "2026-11-15" });
    expect(leerRango(new URLSearchParams("desde=2020-01-01&hasta=2026-11-15"), "2026-11-15")).toEqual({ desde: "2026-10-17", hasta: "2026-11-15" });
  });
});

describe("periodo — mensajeRangoInvalido (CR-FE-4: el 400 RANGO_INVALIDO no trae la causa)", () => {
  const hoy = "2026-11-15";

  it("nombra la causa que encuentra la validación local: más de un año, fecha futura o «Hasta» anterior a «Desde»", () => {
    expect(mensajeRangoInvalido({ desde: sumarDias(hoy, -366), hasta: hoy }, hoy)).toBe(MENSAJE_RANGO_MAXIMO);
    expect(mensajeRangoInvalido({ desde: "2026-11-01", hasta: "2026-11-16" }, hoy)).toBe(MENSAJE_HASTA_FUTURO);
    expect(mensajeRangoInvalido({ desde: "2026-11-10", hasta: "2026-11-01" }, hoy)).toBe(MENSAJE_HASTA_ANTERIOR);
  });

  it("si el rango es válido en el cliente (p. ej. relojes desfasados), usa el texto genérico que cubre las tres causas", () => {
    expect(mensajeRangoInvalido({ desde: "2026-10-17", hasta: hoy }, hoy)).toBe(MENSAJE_RANGO_GENERICO);
    expect(MENSAJE_RANGO_GENERICO).toMatch(/un año/);
    expect(MENSAJE_RANGO_GENERICO).toMatch(/futuras/);
    expect(MENSAJE_RANGO_GENERICO).toMatch(/inicial/);
  });
});
