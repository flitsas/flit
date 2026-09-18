// HU #11194 — las vigencias se muestran en el día real.
//
// Las vigencias son `DateOnly` en el backend y llegan como "2026-07-01". `new Date(...)` las
// interpreta como medianoche UTC y, al formatear en América/Bogotá (UTC−5), salía el día anterior.
// El formulario mostraba la fecha correcta porque el <input type="date"> recibe la cadena cruda.
//
// La Épica #12552 cambió el formato a día/mes/año (y añadió hora a los instantes), pero esta
// regresión sigue vigente palabra por palabra: una fecha de calendario NO se convierte de zona.
// Es la excepción RN-08, y el formateador que le corresponde es `formatFechaCalendario`.
import { describe, expect, it } from "vitest";
import { formatFechaCalendario, formatFechaHora } from "@/lib/format/date";

describe("formatFechaCalendario — HU #11194 (fechas de calendario)", () => {
  it("AC1 una vigencia de escritura se muestra en su día real", () => {
    expect(formatFechaCalendario("2026-07-01")).toBe("01/07/2026");
    expect(formatFechaCalendario("2026-08-31")).toBe("31/08/2026");
  });

  it("AC1 el caso reportado ya no resta un día", () => {
    // Antes: 30/06/2026 – 30/08/2026 en consulta contra 01/07/2026 – 31/08/2026 en edición.
    expect(formatFechaCalendario("2026-07-01")).not.toBe("30/06/2026");
    expect(formatFechaCalendario("2026-08-31")).not.toBe("30/08/2026");
  });

  it("AC2 consulta y edición coinciden: se conserva el día que recibe el input date", () => {
    const desdeApi = "2026-07-01";
    const [year, month, day] = desdeApi.split("-");
    expect(formatFechaCalendario(desdeApi)).toBe(`${day}/${month}/${year}`);
  });

  it("AC3 alcanza a cualquier vigencia, no solo a las escrituras", () => {
    // Firma del baúl e identidad usan el mismo formateador y el mismo tipo DateOnly.
    expect(formatFechaCalendario("2027-01-01")).toBe("01/01/2027");
    expect(formatFechaCalendario("2026-12-31")).toBe("31/12/2026");
  });

  it("AC4 los instantes siguen convirtiéndose a la hora de Colombia", () => {
    // 2026-07-01T02:00:00Z son las 21:00 del 30 de junio en Bogotá: aquí SÍ se resta el día,
    // porque es un momento en el tiempo, no una fecha de calendario.
    expect(formatFechaCalendario("2026-07-01T02:00:00Z")).toBe("30/06/2026");
    expect(formatFechaHora("2026-07-01T02:00:00Z")).toBe("30/06/2026 21:00");
  });

  it("AC4 un Date sigue tratándose como instante", () => {
    expect(formatFechaCalendario(new Date("2026-07-01T02:00:00Z"))).toBe("30/06/2026");
  });

  it("una fecha de calendario no se corre ni pasando por el formateador de instantes", () => {
    // Guarda añadida por la Épica #12552: si alguien clasifica mal el campo, el resultado sale
    // sin hora pero con el día correcto, en vez de retroceder una jornada.
    expect(formatFechaHora("2026-07-01")).toBe("01/07/2026");
  });

  it("valores vacíos o inválidos devuelven el respaldo", () => {
    expect(formatFechaCalendario(null)).toBe("—");
    expect(formatFechaCalendario(undefined)).toBe("—");
    expect(formatFechaCalendario("")).toBe("—");
    expect(formatFechaCalendario("no-es-fecha")).toBe("—");
    expect(formatFechaCalendario("2026-13-45")).toBe("—");
  });
});
