import { describe, expect, it } from "vitest";
import {
  canalDeIngesta,
  construirCron,
  describirProgramacion,
  ejecucionManual,
  errorProgramacion,
  leerCron,
  PROGRAMACION_INICIAL,
  siguienteHora,
  type Programacion,
} from "@/lib/programacion";

const p = (cambios: Partial<Programacion>): Programacion => ({ ...PROGRAMACION_INICIAL, ...cambios });

describe("programación con días y horas (HU #13814)", () => {
  it("AC1: lee las programaciones de fábrica sin mostrar cron", () => {
    expect(leerCron("0 6 * * *")).toMatchObject({ modo: "diario", horas: ["06:00"] });
    expect(leerCron("*/15 * * * *")).toMatchObject({ modo: "minutos", cada: 15 });
    expect(leerCron("0 17 * * 5")).toMatchObject({ modo: "semana", dias: [5], horas: ["17:00"] });
    expect(leerCron("0 18 L * *")).toMatchObject({ modo: "ultimoDia", horas: ["18:00"] });
    expect(leerCron("0 3 * * *")).toMatchObject({ modo: "diario", horas: ["03:00"] });
  });

  it("AC2: lunes a viernes a las 17:00 se guarda como rango y se describe igual que la tabla", () => {
    const lv = p({ modo: "semana", dias: [5, 1, 3, 2, 4], horas: ["17:00"] });
    expect(construirCron(lv)).toBe("0 17 * * 1-5");
    expect(describirProgramacion(lv)).toBe("De lunes a viernes a las 17:00");
    const sueltos = p({ modo: "semana", dias: [0, 3, 1], horas: ["07:30"] });
    expect(construirCron(sueltos)).toBe("30 7 * * 1,3,0");
    expect(describirProgramacion(sueltos)).toBe("Los lunes, miércoles y domingos a las 7:30");
  });

  it("AC3: cada N minutos y fin de mes, ida y vuelta", () => {
    for (const cron of ["*/15 * * * *", "0 18 L * *", "0 7,13 * * *", "0 9 15 * *", "0 6 * * 1-5", "0 17 * * 5"]) {
      const leida = leerCron(cron);
      expect(leida).not.toBeNull();
      expect(construirCron(leida as Programacion)).toBe(cron);
    }
    expect(describirProgramacion(p({ modo: "minutos", cada: 15 }))).toBe("Cada 15 minutos");
    expect(describirProgramacion(p({ modo: "ultimoDia", horas: ["18:00"] }))).toBe("El último día de cada mes a las 18:00");
    expect(describirProgramacion(p({ modo: "diario", horas: ["13:00", "07:00"] }))).toBe("Todos los días a las 7:00 y 13:00");
    expect(describirProgramacion(p({ modo: "diario", horas: ["01:00"] }))).toBe("Todos los días a la 1:00");
  });

  it("AC4: dice qué falta antes de guardar", () => {
    expect(errorProgramacion(p({ modo: "semana", dias: [] }))).toBe("Marca al menos un día de la semana.");
    expect(errorProgramacion(p({ horas: [] }))).toBe("Agrega al menos una hora.");
    expect(errorProgramacion(p({ horas: ["07:00", "13:30"] }))).toMatch(/mismos minutos/);
    expect(errorProgramacion(p({ horas: [""] }))).toBe("Completa todas las horas.");
    expect(errorProgramacion(p({ modo: "diaMes", diaMes: 31 }))).toBe("El día del mes va de 1 a 28.");
    expect(errorProgramacion(PROGRAMACION_INICIAL)).toBeNull();
  });

  it("AC5: lo que el selector no sabe representar queda para mantener o reemplazar", () => {
    for (const cron of ["0 8-18/2 * * *", "0 6 1 6 *", "0 6 15 * 1", "*/15 8 * * *", "0 6 31 * *", "basura"]) expect(leerCron(cron)).toBeNull();
  });

  it("«Otra hora» propone la siguiente y el resumen no repite horas", () => {
    expect(siguienteHora(["06:00"])).toBe("07:00");
    expect(siguienteHora(["23:30"])).toBe("00:30");
    expect(describirProgramacion(p({ horas: ["06:00", "06:00"] }))).toBe("Todos los días a las 6:00");
  });

  it("los siete días son «todos los días»", () => {
    expect(leerCron("0 6 * * 0-6")).toMatchObject({ modo: "diario" });
    expect(construirCron(p({ modo: "semana", dias: [0, 1, 2, 3, 4, 5, 6] }))).toBe("0 6 * * *");
  });
});

describe("ejecutar a mano (HU #13815)", () => {
  it("AC2: avisa de correos reales en avisos y reportes", () => {
    expect(ejecucionManual("AvisosConductor").correos).toBe(true);
    expect(ejecucionManual("ReporteSemanal").correos).toBe(true);
    expect(ejecucionManual("ReporteMensual").correos).toBe(true);
    expect(ejecucionManual("ReintentoContraventor").correos).toBe(false);
    expect(ejecucionManual("SincronizacionOrganismos")).toMatchObject({ correos: false, porEmpresa: false });
  });

  it("AC3: la ingesta va al diálogo de ingesta con su canal", () => {
    expect(canalDeIngesta("IngestaMunicipalPlaca")).toBe("MunicipalPlaca");
    expect(canalDeIngesta("ReporteSemanal")).toBeNull();
  });
});
