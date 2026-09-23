import type { QueryCondition } from '@/lib/api/queries';
import { FILTRO_RECHAZADO_PREASIGNACION, type EstadoFiltro } from './estados';

/**
 * Epic #12686 (HU #12806) — atajos de la «Búsqueda rápida» del listado del gestor y del SuperAdmin.
 *
 * <p>Cada atajo se traduce a lo que el servidor ya entiende: un estado, una condición oculta del
 * catálogo o el parámetro `busquedaRapida` (HU #12805). Ninguno entra en «+ Filtro»: el filtro
 * avanzado del usuario no se toca. Cuando el atajo es de un solo estado, ese estado queda como el
 * filtro de la tira, así su tarjeta se resalta.</p>
 *
 * <p>«Mis trámites» son los que el usuario tiene a su cargo HOY (opción B, 2026-09-23): si Ana crea
 * un trámite y se lo pasan a Carlos, aparece en el de Carlos. No se ofrece al SuperAdmin, que no
 * tiene trámites a su cargo: el atajo le saldría siempre vacío.</p>
 */
export type AtajoGestor =
  | 'en_subsanacion'
  | 'rechazado_preasignacion'
  | 'mas_de_5_dias'
  | 'mas_de_10_dias'
  | 'sin_firmas'
  | 'sin_documento'
  | 'pausados'
  | 'faltantes_por_aprobar'
  | 'mis_tramites';

export interface AtajoDef<K extends string = string> {
  key: K;
  label: string;
  /** Qué trae, para el título del control. */
  hint: string;
  /** Estado de la tira que fija el atajo (y con él, la tarjeta resaltada). */
  estado?: EstadoFiltro;
  /** Parámetro que resuelve el servidor. */
  busquedaRapida?: string;
  /** Condiciones del catálogo que se suman sin aparecer en «+ Filtro». */
  condiciones?: QueryCondition[];
}

export const ATAJOS_GESTOR: readonly AtajoDef<AtajoGestor>[] = [
  {
    key: 'en_subsanacion',
    label: 'En subsanación',
    hint: 'Rechazados con la subsanación activa',
    condiciones: [{ fieldId: 'en_subsanacion', operator: 'es_alguno', values: ['true'] }],
  },
  {
    key: 'rechazado_preasignacion',
    label: 'Rechazado desde preasignación',
    hint: 'Rechazados por el organismo antes de asignar la placa',
    estado: FILTRO_RECHAZADO_PREASIGNACION,
  },
  {
    key: 'mas_de_5_dias',
    label: 'Más de 5 días en gestión',
    hint: 'Entregados que el organismo lleva más de 5 días sin decidir',
    estado: 'entregado',
    busquedaRapida: 'mas_de_5_dias',
  },
  {
    key: 'mas_de_10_dias',
    label: 'Más de 10 días en gestión',
    hint: 'Entregados que el organismo lleva más de 10 días sin decidir',
    estado: 'entregado',
    busquedaRapida: 'mas_de_10_dias',
  },
  {
    key: 'sin_firmas',
    label: 'Trámites sin firmas',
    hint: 'Borradores con alguna parte sin validar identidad ni firma de baúl vigente',
    estado: 'borrador',
    busquedaRapida: 'sin_firmas',
  },
  {
    key: 'mis_tramites',
    label: 'Mis trámites',
    hint: 'Trámites que tienes a tu cargo hoy, los hayas creado o te los hayan asignado',
    busquedaRapida: 'mis_tramites',
  },
  {
    key: 'faltantes_por_aprobar',
    label: 'Faltantes por aprobar',
    hint: 'Entregados a la espera de la decisión del organismo',
    estado: 'entregado',
  },
  {
    key: 'sin_documento',
    label: 'Sin documento',
    hint: 'Borradores a los que les falta al menos un documento obligatorio',
    estado: 'borrador',
    busquedaRapida: 'sin_documento',
  },
  {
    key: 'pausados',
    label: 'Trámites pausados',
    hint: 'Borradores que no pueden salir al organismo: sin firmas, sin documento o pausados',
    estado: 'borrador',
    busquedaRapida: 'pausados',
  },
];

/** Atajos de la perspectiva: el SuperAdmin no ve «Mis trámites». */
export function atajosDePerspectiva(esSuperAdmin: boolean): readonly AtajoDef<AtajoGestor>[] {
  return esSuperAdmin ? ATAJOS_GESTOR.filter((a) => a.key !== 'mis_tramites') : ATAJOS_GESTOR;
}

export function atajoGestor(key: AtajoGestor | ''): AtajoDef<AtajoGestor> | undefined {
  return key ? ATAJOS_GESTOR.find((a) => a.key === key) : undefined;
}
