import { describe, expect, it } from "vitest";
import { narracionDesdeInfraccion } from "@/components/detalle/narracion";

describe("narración del timeline (rediseño S1)", () => {
  it("cuenta días calendario de Colombia desde la infracción", () => {
    expect(narracionDesdeInfraccion("2025-10-03", "2025-10-14T15:00:00Z")).toBe("11 días después de la infracción");
    expect(narracionDesdeInfraccion("2025-10-03", "2025-10-04T15:00:00Z")).toBe("1 día después de la infracción");
    // 03 oct a las 23:30 de Colombia sigue siendo el día de la infracción aunque en UTC ya sea el 4.
    expect(narracionDesdeInfraccion("2025-10-03", "2025-10-04T04:30:00Z")).toBe("el mismo día de la infracción");
  });

  it("sin fecha de imposición o con un evento anterior, solo queda la fecha absoluta", () => {
    expect(narracionDesdeInfraccion(null, "2025-10-14T15:00:00Z")).toBeNull();
    expect(narracionDesdeInfraccion("2025-10-03", "2025-09-30T15:00:00Z")).toBeNull();
    expect(narracionDesdeInfraccion("2025-10-03", "no es fecha")).toBeNull();
  });
});
