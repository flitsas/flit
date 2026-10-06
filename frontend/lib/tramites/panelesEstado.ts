import type { ProcedureFamily } from '@/lib/api/types/procedure-parametrization';
import { ESTADOS_RUTA_PLACA, type EstadoFiltro, type EstadoTramite } from './estados';

/**
 * Epic #12686 (HU #12802) — tarjetas de la tira de estados según quién mira y qué familia.
 *
 * <p><b>Perspectiva.</b> El SuperAdmin («Admin FLIT» en la épica) es el único que ve Preparado. El
 * administrador de una compañía gestora y la vista de red son perspectiva de gestor. «Subsanación» y
 * «Rechazado preasignación» ya no son tarjetas: pasan a la búsqueda rápida. Quitar Preparado al
 * gestor solo quita la tarjeta; sus trámites en ese estado siguen en la tabla.</p>
 *
 * <p><b>Familia.</b> Traspaso y Otros trámites no recorren la ruta de placa, así que en esas pestañas
 * no hay Preasignación ni Asignado (decisión de la épica: lista fija por familia, no derivada de la
 * configuración de cada tipo).</p>
 */
export type PerspectivaListado = 'gestor' | 'superadmin';

const PANELES_POR_PERSPECTIVA: Record<PerspectivaListado, readonly EstadoFiltro[]> = {
  gestor: [
    'borrador',
    'preasignacion',
    'asignado',
    'entregado',
    'aprobado',
    'rechazado',
    'revocado',
    'anulado',
  ],
  superadmin: [
    'borrador',
    'preparado',
    'preasignacion',
    'asignado',
    'entregado',
    'aprobado',
    'rechazado',
    'revocado',
    'anulado',
  ],
};

/** Familias cuyo flujo no pasa por la ruta de placa. */
const FAMILIAS_SIN_RUTA_DE_PLACA: readonly ProcedureFamily[] = ['TRASPASO', 'OTROS'];

/** ¿La familia de la pestaña recorre este estado? `''` = pestaña Todos. */
export function familiaUsaEstado(familia: '' | ProcedureFamily, estado: string): boolean {
  if (familia === '' || !FAMILIAS_SIN_RUTA_DE_PLACA.includes(familia)) return true;
  return !ESTADOS_RUTA_PLACA.includes(estado as EstadoTramite);
}

/** Tarjetas de la tira, en orden, para la perspectiva y la pestaña de familia. */
export function panelesDeEstado(
  perspectiva: PerspectivaListado,
  familia: '' | ProcedureFamily,
): readonly EstadoFiltro[] {
  return PANELES_POR_PERSPECTIVA[perspectiva].filter((e) => familiaUsaEstado(familia, e));
}
