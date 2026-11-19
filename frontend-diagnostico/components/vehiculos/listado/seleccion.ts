import { MAXIMO_DEUDA_VARIAS, MAXIMO_SOLICITUD_MASIVA } from "@/lib/saneamiento/api";
import { ROTULO_MOTIVO_NO_ELEGIBLE } from "@/lib/saneamiento/estados";
import type { FilaVehiculoListado } from "@/lib/vehiculos/listado";

// PS-3 (CR-5): reglas puras de la selección por página: para qué acción cuenta cada fila y por qué una acción de la barra
// queda bloqueada. Sin estado ni efectos.

/** D-108: la casilla de una placa en proceso, para quien solo puede descargar, dice lo mismo que la bandera. */
const MOTIVO_EN_PROCESO = ROTULO_MOTIVO_NO_ELEGIBLE.NO_TERMINADO;

export const terminado = (f: FilaVehiculoListado) => f.estado === "TERMINADO";

/** Selección de una fila: para qué acción cuenta y, deshabilitada, por qué (unión con « · » sin repetir). */
export interface SeleccionFila {
  habilitada: boolean;
  sanear: boolean;
  descargar: boolean;
  motivo?: string;
}

export function seleccionDe(f: FilaVehiculoListado, puedeSanear: boolean, puedeDescargar: boolean): SeleccionFila {
  const sanear = puedeSanear && f.saneamiento?.elegible === true;
  const descargar = puedeDescargar && terminado(f);
  const habilitada = sanear || descargar;
  if (habilitada) return { habilitada, sanear, descargar };
  const motivos: string[] = [];
  const m = f.saneamiento?.motivo_no_elegible;
  if (puedeSanear && m && Object.prototype.hasOwnProperty.call(ROTULO_MOTIVO_NO_ELEGIBLE, m)) motivos.push(ROTULO_MOTIVO_NO_ELEGIBLE[m]);
  if (puedeDescargar && !terminado(f)) motivos.push(MOTIVO_EN_PROCESO);
  const unicos = [...new Set(motivos)];
  return { habilitada, sanear, descargar, motivo: unicos.length ? unicos.join(" · ") : undefined };
}

/** Motivo por el que «Sanear (N)» no se puede usar, o null. */
export function bloqueoSanear(n: number): string | null {
  if (n === 0) return "Ninguno de los seleccionados se puede sanear";
  if (n > MAXIMO_SOLICITUD_MASIVA) return `Máximo ${MAXIMO_SOLICITUD_MASIVA} vehículos por solicitud`;
  return null;
}

/** Motivo por el que «Descargar deuda (N)» no se puede usar, o null. */
export function bloqueoDescargar(n: number): string | null {
  if (n === 0) return "Ninguno de los seleccionados tiene la deuda lista";
  if (n > MAXIMO_DEUDA_VARIAS) return `Máximo ${MAXIMO_DEUDA_VARIAS} placas por descarga`;
  return null;
}
