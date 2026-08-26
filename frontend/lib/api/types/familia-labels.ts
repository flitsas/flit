import type { ProcedureFamily } from './procedure-parametrization';

/**
 * Etiqueta legible de la familia de un trámite (ADR-0050).
 *
 * Fuente ÚNICA: antes este mapa estaba duplicado literalmente en cinco archivos —dos de reportes
 * OT, uno de reportes de compañía, la lista de trámites asociados y el panel de validaciones—, cada
 * uno con las dos modalidades escritas a mano. Al pasar el backend a familias había que tocar los
 * cinco, y bastaba olvidar uno para que una pantalla mostrara el código crudo.
 *
 * Se aceptan también los dos valores de la difunta `modalidad_entrada`, porque siguen llegando en
 * exportes y datos históricos. Ese puente desaparece cuando dejen de existir.
 */
const ETIQUETAS: Record<string, string> = {
  MATRICULAS: 'Matrículas',
  TRASPASO: 'Traspaso',
  OTROS: 'Otros trámites',
  // Puente con los valores heredados.
  matricula_inicial: 'Matrícula inicial',
  traspaso: 'Traspaso',
};

/**
 * Etiqueta de la familia. Un valor desconocido se devuelve tal cual en vez de mostrarse vacío: es
 * más útil ver el código crudo que una celda en blanco.
 */
export function familiaLabel(valor: string | null | undefined): string {
  if (!valor) return '';
  return ETIQUETAS[valor] ?? ETIQUETAS[valor.trim().toUpperCase()] ?? valor;
}

/** Opciones de familia para filtros y tabs, en el orden de presentación. */
export const FAMILIA_OPCIONES: { value: ProcedureFamily; label: string }[] = [
  { value: 'MATRICULAS', label: 'Matrículas' },
  { value: 'TRASPASO', label: 'Traspaso' },
  { value: 'OTROS', label: 'Otros trámites' },
];
