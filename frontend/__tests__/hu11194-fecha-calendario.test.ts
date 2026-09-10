// HU #11194 — las vigencias se muestran en el día real.
//
// Las vigencias son `DateOnly` en el backend y llegan como "2026-07-01". `new Date(...)` las
// interpreta como medianoche UTC y, al formatear en América/Bogotá (UTC−5), salía el día anterior.
// El formulario mostraba la fecha correcta porque el <input type="date"> recibe la cadena cruda.
import { describe, expect, it } from "vitest";
import { formatFecha, formatFechaHora } from "@/lib/format/date";

describe("formatFecha — HU #11194 (fechas de calendario)", () => {
  it("AC1 una vigencia de escritura se muestra en su día real", () => {
    expect(formatFecha("2026-07-01")).toBe("2026/07/01");
    expect(formatFecha("2026-08-31")).toBe("2026/08/31");
  });

  it("AC1 el caso reportado ya no resta un día", () => {
    // Antes: 2026/06/30 – 2026/08/30 en consulta contra 2026/07/01 – 2026/08/31 en edición.
    expect(formatFecha("2026-07-01")).not.toBe("2026/06/30");
    expect(formatFecha("2026-08-31")).not.toBe("2026/08/30");
  });

  it("AC2 consulta y edición coinciden: se conserva la cadena que recibe el input date", () => {
    const desdeApi = "2026-07-01";
    const [year, month, day] = desdeApi.split("-");
    expect(formatFecha(desdeApi)).toBe(`${year}/${month}/${day}`);
  });

  it("AC3 alcanza a cualquier vigencia, no solo a las escrituras", () => {
    // Firma del baúl e identidad usan el mismo formateador y el mismo tipo DateOnly.
    expect(formatFecha("2027-01-01")).toBe("2027/01/01");
    expect(formatFecha("2026-12-31")).toBe("2026/12/31");
  });

  it("AC4 los instantes siguen convirtiéndose a la hora de Colombia", () => {
    // 2026-07-01T02:00:00Z son las 21:00 del 30 de junio en Bogotá: aquí SÍ se resta el día,
    // porque es un momento en el tiempo, no una fecha de calendario.
    expect(formatFecha("2026-07-01T02:00:00Z")).toBe("2026/06/30");
    expect(formatFechaHora("2026-07-01T02:00:00Z")).toBe("2026/06/30 21:00");
  });

  it("AC4 un Date sigue tratándose como instante", () => {
    expect(formatFecha(new Date("2026-07-01T02:00:00Z"))).toBe("2026/06/30");
  });

  it("valores vacíos o inválidos devuelven el respaldo", () => {
    expect(formatFecha(null)).toBe("—");
    expect(formatFecha(undefined)).toBe("—");
    expect(formatFecha("")).toBe("—");
    expect(formatFecha("no-es-fecha")).toBe("—");
    expect(formatFecha("2026-13-45")).toBe("—");
  });
});
