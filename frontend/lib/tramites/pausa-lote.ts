import type { ModoSeleccionLote } from '@/hooks/useSeleccionLote';
import { isNetworkReadOnly, type NetworkScopeItem } from '@/lib/tramites/network-scope';

/**
 * HU #13380 (épica #13216) — la pausa ICT en lote sale de la MISMA selección que la descarga
 * masiva. Antes la fila de un borrador ICT llevaba dos casillas idénticas (descarga y pausa) y el
 * usuario confundía la de pausa con la marca de «Pausado». Ahora hay una casilla y una barra; este
 * módulo decide, sobre esa selección, qué se puede pausar y qué queda fuera (y por qué).
 */

/** Lo mínimo de una fila que la regla necesita. */
export interface ItemPausaLote extends NetworkScopeItem {
  id: string;
  origin?: string | null;
  estado?: string | null;
  isPaused?: boolean | null;
}

/**
 * Con «Seleccionar todos (del filtro)» los ids no se conocen en el cliente (los resuelve el
 * servidor al crear el lote), así que la pausa en lote no se ofrece en ese modo. Decisión por
 * defecto del orquestador: se aísla aquí para poder cambiarla en un solo punto.
 */
export const PAUSA_LOTE_EN_MODO_FILTRO = false;

/** Nota de la barra cuando la selección es «todos los del filtro». */
export const NOTA_PAUSA_MODO_FILTRO = 'La pausa en lote aplica a trámites marcados uno a uno';

export function pausaLoteDisponibleEnModo(modo: ModoSeleccionLote): boolean {
  return modo === 'ids' || PAUSA_LOTE_EN_MODO_FILTRO;
}

/** Solo los borradores ICT se pausan (misma regla que la acción por fila). */
export function esBorradorIct(item: Pick<ItemPausaLote, 'origin' | 'estado'>): boolean {
  return item.origin === 'ict' && item.estado === 'borrador';
}

export interface ParticionPausaLote<T> {
  /** Borradores ICT propios: los únicos que viajan a `pauseInstancesMassive`. */
  pausables: T[];
  /** Borradores ICT de la red (modo consulta): excluidos de las acciones masivas. */
  excluidosRed: number;
  /** Seleccionados que no son borradores ICT (o cuyo resumen no se conoce): no se pausan. */
  excluidosNoIct: number;
}

/**
 * Reparte los ids seleccionados. `buscar` resuelve cada id a su fila (la de la página vigente o la
 * última vista al marcarla); un id sin fila conocida no se puede verificar y cuenta como no ICT.
 */
export function partirSeleccionParaPausa<T extends ItemPausaLote>(
  ids: readonly string[],
  buscar: (id: string) => T | undefined,
  currentTenantId: string | null | undefined,
): ParticionPausaLote<T> {
  const pausables: T[] = [];
  let excluidosRed = 0;
  let excluidosNoIct = 0;
  for (const id of ids) {
    const item = buscar(id);
    if (!item || !esBorradorIct(item)) excluidosNoIct += 1;
    else if (isNetworkReadOnly(item, currentTenantId)) excluidosRed += 1;
    else pausables.push(item);
  }
  return { pausables, excluidosRed, excluidosNoIct };
}

/** La sección de pausa de la barra solo aparece si la selección tiene algún borrador ICT. */
export function seleccionTieneBorradorIct(p: ParticionPausaLote<unknown>): boolean {
  return p.pausables.length + p.excluidosRed > 0;
}

/**
 * Bug #13109 (punto 3) — como el menú por fila: ninguno pausado → Pausar; todos → Reanudar;
 * mezcla → ninguna.
 */
export function accionPausaLote(
  pausables: readonly Pick<ItemPausaLote, 'isPaused'>[],
): 'pausar' | 'reanudar' | null {
  if (pausables.length === 0) return null;
  const pausados = pausables.filter((it) => it.isPaused).length;
  if (pausados === 0) return 'pausar';
  if (pausados === pausables.length) return 'reanudar';
  return null;
}

/** Texto de los excluidos de la pausa por no ser borradores ICT (la exclusión no es muda). */
export function textoExcluidosNoIct(excluidos: number): string | null {
  if (excluidos <= 0) return null;
  return `${excluidos} excluido${excluidos === 1 ? '' : 's'} de la pausa: no son borradores ICT`;
}
