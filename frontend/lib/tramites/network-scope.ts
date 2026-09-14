import type { InstanceSummary, ProcedureInstanceDetail } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12362 — modo consulta transversal (Feature #12257, épica Concesión).
 *
 * Un trámite de un cliente HIJO abierto por la cabeza de red se ve, pero no se toca. Este módulo
 * es la ÚNICA fuente de esa decisión en el frontend: fila, modal de detalle y asistente la consumen
 * de aquí para que no puedan divergir. La defensa real vive en el servidor (#12358); esto evita que
 * el usuario la choque a base de 403.
 */

/** Dueño del trámite tal como lo devuelven las rutas `/api/v1/tramites/network/**` (#12358). */
export interface NetworkOwned {
  tenantId: string;
  /** Razón social del cliente hijo dueño del trámite. */
  tenantName: string;
}

/**
 * Marca de PROCEDENCIA: la pone `tramitesClient` sobre cada ítem que llega por una ruta
 * `network/**`. Es un campo del cliente, no del contrato — por eso es opcional y va aparte de
 * `NetworkOwned`.
 */
export interface NetworkProvenance {
  fromNetwork?: boolean;
}

export type NetworkInstanceSummary = InstanceSummary & NetworkOwned & NetworkProvenance;
export type NetworkInstanceDetail = ProcedureInstanceDetail & NetworkOwned & NetworkProvenance;

/** Lo mínimo que hace falta mirar de un ítem para decidir el modo consulta. */
export type NetworkScopeItem = {
  tenantId?: string | null;
} & NetworkProvenance;

/**
 * ¿Este ítem está «en modo consulta» para el usuario actual?
 *
 * Verdadero cuando el ítem PROVIENE de una ruta `network/**` o cuando su `tenantId` existe y es
 * distinto del tenant del usuario. Con `currentTenantId` vacío (SuperAdmin, sesión sin claim) la
 * segunda regla no aplica: el SuperAdmin ve trámites de todas las compañías y ninguno es «de la
 * red» — su alcance lo decide el servidor por rol, no por jerarquía. Un cliente sin jerarquía
 * nunca recibe ítems de otro tenant, así que para él la función es siempre falsa (AC5).
 */
export function isNetworkReadOnly(
  item: NetworkScopeItem | null | undefined,
  currentTenantId: string | null | undefined,
): boolean {
  if (!item) return false;
  if (item.fromNetwork === true) return true;
  if (!currentTenantId) return false;
  const owner = item.tenantId?.trim();
  if (!owner) return false;
  return owner.toLowerCase() !== currentTenantId.trim().toLowerCase();
}

/**
 * Códigos con los que el servidor dice «esto no es de tu alcance» sobre un trámite de la red. El
 * 403 es el rechazo explícito; el 404 escueto es el patrón anti-enumeración de documentos de red
 * (plan #12257, decisión 6). Ninguno de los dos es un fallo técnico que el usuario deba ver.
 */
const SCOPE_STATUSES = new Set([403, 404]);

/** `true` si el error es un rechazo de alcance (403/404) y no un fallo técnico. */
export function isScopeRejection(err: unknown): boolean {
  if (!err || typeof err !== 'object') return false;
  const status = (err as { status?: unknown }).status;
  return typeof status === 'number' && SCOPE_STATUSES.has(status);
}

/** Copy único del AC3: se muestra sin detalle técnico y sin botón de reintento. */
export const COPY_DOCUMENTOS_FUERA_DE_ALCANCE =
  'Los documentos del cliente hijo no forman parte de tu alcance de consulta';

export const COPY_SECCION_FUERA_DE_ALCANCE =
  'Esta información del cliente hijo no forma parte de tu alcance de consulta';

/** Etiqueta del distintivo visible (texto + icono; nunca solo color). */
export const ETIQUETA_SOLO_CONSULTA = 'Solo consulta';

/**
 * Resultado de una carga de sección en modo consulta: o un error técnico normal (con reintento) o
 * un rechazo de alcance (copy amable, sin reintento).
 */
export interface ErrorDeSeccion {
  mensaje: string;
  fueraDeAlcance: boolean;
}

export function describirErrorDeSeccion(
  err: unknown,
  consultaMode: boolean,
  fallback: string,
  copyFueraDeAlcance: string = COPY_SECCION_FUERA_DE_ALCANCE,
): ErrorDeSeccion {
  if (consultaMode && isScopeRejection(err)) {
    return { mensaje: copyFueraDeAlcance, fueraDeAlcance: true };
  }
  return { mensaje: err instanceof Error ? err.message : fallback, fueraDeAlcance: false };
}

/**
 * AC6 — reparte una selección entre trámites propios (accionables) y de la red (excluidos).
 * Devuelve los ids propios y cuántos quedaron fuera para que la barra lo diga con su motivo.
 */
export function partirSeleccionPorAlcance<T extends { id: string } & NetworkScopeItem>(
  items: readonly T[],
  selectedIds: ReadonlySet<string>,
  currentTenantId: string | null | undefined,
): { propios: T[]; excluidos: number } {
  const propios: T[] = [];
  let excluidos = 0;
  for (const it of items) {
    if (!selectedIds.has(it.id)) continue;
    if (isNetworkReadOnly(it, currentTenantId)) excluidos += 1;
    else propios.push(it);
  }
  return { propios, excluidos };
}

/** Texto de la barra de acciones masivas cuando hay excluidos (AC6). */
export function textoExcluidosRed(excluidos: number): string | null {
  if (excluidos <= 0) return null;
  return `${excluidos} excluido${excluidos === 1 ? '' : 's'}: trámites de la red (solo consulta)`;
}
