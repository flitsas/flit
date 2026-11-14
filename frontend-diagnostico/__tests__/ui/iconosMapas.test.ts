import { describe, expect, it } from "vitest";
import { MAPA_ICONO } from "@flit/ui/iconos-mapas";
import { GLIFOS } from "@flit/ui/iconos-kit";

// Uso de ejemplo: <CabeceraModulo icono="pago" … /> resuelve el glifo con MAPA_ICONO["pago"] (D-64, saneamiento PS-4/PS-5).

describe("MAPA_ICONO · clave «pago» (D-64)", () => {
  it("mapea «pago» al glifo «pago» del kit, no al «documento» de respaldo", () => {
    expect(MAPA_ICONO.pago).toBe("pago");
  });

  it("el glifo existe en el kit", () => {
    expect(GLIFOS).toHaveProperty("pago");
  });
});
