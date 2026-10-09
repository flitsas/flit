// Vista anual del calendario hábil (§7.25): utilidades puras para armar la grilla de cada mes.

export interface DiaCalendario {
  fecha: string;
  esHabil: boolean;
  esFinDeSemana: boolean;
  esFestivo: boolean;
  descripcionFestivo: string | null;
  origen: string;
}

export interface AnioCalendario {
  anio: number;
  habiles: number;
  festivos: number;
  dias: DiaCalendario[];
}

export const DIAS_SEMANA = ["L", "M", "X", "J", "V", "S", "D"];

/**
 * Semanas del mes (lunes a domingo) con huecos `null` antes del primer día y después del último. `mes` es 0-11.
 * Los días que no vengan de la API (fuera de rango) también quedan como null.
 */
export function semanasDelMes(dias: DiaCalendario[], anio: number, mes: number): (DiaCalendario | null)[][] {
  const prefijo = `${anio}-${String(mes + 1).padStart(2, "0")}-`;
  const porFecha = new Map(dias.filter((d) => d.fecha.startsWith(prefijo)).map((d) => [d.fecha, d]));
  const total = new Date(Date.UTC(anio, mes + 1, 0)).getUTCDate();
  const primero = (new Date(Date.UTC(anio, mes, 1)).getUTCDay() + 6) % 7; // lunes = 0
  const celdas: (DiaCalendario | null)[] = Array.from({ length: primero }, () => null);
  for (let d = 1; d <= total; d++) celdas.push(porFecha.get(`${prefijo}${String(d).padStart(2, "0")}`) ?? null);
  while (celdas.length % 7 !== 0) celdas.push(null);
  const semanas: (DiaCalendario | null)[][] = [];
  for (let i = 0; i < celdas.length; i += 7) semanas.push(celdas.slice(i, i + 7));
  return semanas;
}

/** Qué se puede hacer con un día: los fines de semana no se tocan; un hábil se marca festivo; un festivo se quita. */
export function accionDia(d: DiaCalendario): "marcar" | "quitar" | null {
  if (d.esFinDeSemana) return null;
  return d.esFestivo ? "quitar" : "marcar";
}

export function tipoDia(d: DiaCalendario): "festivo" | "finDeSemana" | "habil" {
  if (d.esFestivo) return "festivo";
  if (d.esFinDeSemana) return "finDeSemana";
  return "habil";
}
