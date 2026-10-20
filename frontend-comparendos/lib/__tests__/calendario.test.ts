import { describe, expect, it } from "vitest";
import { accionDia, semanasDelMes, type DiaCalendario } from "@/lib/calendario";

const dia = (fecha: string, extra: Partial<DiaCalendario> = {}): DiaCalendario => ({
  fecha,
  esHabil: true,
  esFinDeSemana: false,
  esFestivo: false,
  descripcionFestivo: null,
  origen: "SEMILLA",
  ...extra,
});

describe("calendario", () => {
  it("arma las semanas de octubre de 2026 empezando el jueves 1", () => {
    const dias = Array.from({ length: 31 }, (_, i) => dia(`2026-10-${String(i + 1).padStart(2, "0")}`));
    const semanas = semanasDelMes(dias, 2026, 9);
    expect(semanas[0]?.slice(0, 3)).toEqual([null, null, null]);
    expect(semanas[0]?.[3]?.fecha).toBe("2026-10-01");
    expect(semanas.every((s) => s.length === 7)).toBe(true);
    expect(semanas.flat().filter(Boolean)).toHaveLength(31);
  });

  it("solo deja marcar hábiles y quitar festivos", () => {
    expect(accionDia(dia("2026-10-10", { esFinDeSemana: true, esHabil: false }))).toBeNull();
    expect(accionDia(dia("2026-10-12", { esFestivo: true, esHabil: false }))).toBe("quitar");
    expect(accionDia(dia("2026-10-14"))).toBe("marcar");
  });
});
