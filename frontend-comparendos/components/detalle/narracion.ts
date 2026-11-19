import { diaColombia } from "@/lib/format/date";

const MS_DIA = 86_400_000;

/**
 * La parte narrada de un evento del timeline (rediseño S1, idea de la app de Johan): cuántos días pasaron desde la
 * infracción, en días calendario de Colombia. Sin fecha de imposición o con un evento anterior a ella, no dice nada y
 * el evento queda solo con su fecha.
 */
export function narracionDesdeInfraccion(fechaImposicion: string | null | undefined, ocurridoEn: string): string | null {
  if (!fechaImposicion || !/^\d{4}-\d{2}-\d{2}/.test(fechaImposicion)) return null;
  const instante = new Date(ocurridoEn);
  if (Number.isNaN(instante.getTime())) return null;
  const dias = Math.round((Date.parse(`${diaColombia(instante)}T00:00:00Z`) - Date.parse(`${fechaImposicion.slice(0, 10)}T00:00:00Z`)) / MS_DIA);
  if (!Number.isFinite(dias) || dias < 0) return null;
  if (dias === 0) return "el mismo día de la infracción";
  return dias === 1 ? "1 día después de la infracción" : `${dias.toLocaleString("es-CO")} días después de la infracción`;
}
