import type {
  Actor,
  ActorDocumentType,
  ActorRol,
  InstanceSummary,
  ProcedureActor,
  ProcedureInstanceDetail,
} from '@/lib/api/types/procedure-runtime';

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

/**
 * HU #12411 — la descarga de un documento de la red es FAIL-CLOSED en auditoría: si el servidor no
 * puede dejar el registro `network.attachments.download`, responde 503 `{ "error": "audit_unavailable" }`
 * y NO entrega el binario. A diferencia del 403/404 de alcance, es transitorio: se muestra como
 * alerta con reintento, sin abrir el visor ni descargar nada.
 */
export const COPY_DESCARGA_SIN_AUDITORIA =
  'No se pudo dejar registro de auditoría de la descarga; inténtalo de nuevo en unos segundos.';

/** `true` si el error es el 503 `audit_unavailable` de la descarga de red (contrato B5 #12410). */
export function isAuditUnavailable(err: unknown): boolean {
  if (!err || typeof err !== 'object') return false;
  const { status, problem } = err as { status?: unknown; problem?: unknown };
  if (status !== 503 || !problem || typeof problem !== 'object') return false;
  return (problem as { error?: unknown }).error === 'audit_unavailable';
}

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

const ROLES_ACTOR: ReadonlySet<string> = new Set<ActorRol>(['comprador', 'vendedor', 'locatario']);

/**
 * HU #12362 — actores del DETALLE CONSOLIDADO (`NetworkProcedureDetailResponse.instance.actors`,
 * el `Actor` embebido: `actorType`/`documentType`/`documentNumber`/`fullName`/`email`) al shape
 * `ProcedureActor` que pinta la sección «Actores» del modal. Se usa SOLO en consulta: para un
 * trámite de un hijo, `GET .../actors` responde 404 por diseño y el detalle consolidado es la única
 * fuente. Lo que el embebido NO trae (teléfono, dirección, ciudad, representante legal, porcentaje)
 * simplemente no se pinta — los campos son opcionales en la tarjeta.
 *
 * `ordinal` no viaja en el embebido: se asigna por posición dentro de cada rol (1..n) respetando
 * el orden del servidor, que ya lista al principal primero. Los `actorType` fuera del vocabulario
 * (`comprador` | `vendedor` | `locatario`) se descartan, igual que hace el backend al leerlos.
 */
export function actoresDesdeDetalleConsolidado(
  actors: readonly Actor[] | null | undefined,
): ProcedureActor[] {
  const siguienteOrdinal = new Map<string, number>();
  const out: ProcedureActor[] = [];
  for (const a of actors ?? []) {
    const rol = (a.actorType ?? '').trim().toLowerCase();
    if (!ROLES_ACTOR.has(rol)) continue;
    const ordinal = siguienteOrdinal.get(rol) ?? 1;
    siguienteOrdinal.set(rol, ordinal + 1);
    out.push({
      rol: rol as ActorRol,
      tipoDocumento: (a.documentType ?? '') as ActorDocumentType,
      numeroDocumento: a.documentNumber ?? '',
      nombreCompleto: a.fullName ?? '',
      email: a.email ?? '',
      ordinal,
    });
  }
  return out;
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

// ── HU #12363 — alcance de lectura del listado (selector «Mi compañía | Toda la red | hijo») ────

/** `own` = solo mi compañía (llamadas idénticas a hoy); `network` = rutas `network/**`. */
export type NetworkScopeMode = 'own' | 'network';

/**
 * Preferencia por USUARIO (scope `tramites.scope` de `/api/v1/me/ui-preferences`): nunca en
 * localStorage, que es por navegador y lo compartirían dos usuarios del mismo cliente (AC5).
 */
export interface NetworkScopePreference {
  mode: NetworkScopeMode;
  /** Un cliente hijo concreto dentro de la red. Solo tiene sentido con `mode: 'network'`. */
  childTenantId?: string;
}

/** AC2 — el alcance por defecto es el propio: la red no se muestra hasta que el usuario la pide. */
export const DEFAULT_NETWORK_SCOPE: NetworkScopePreference = { mode: 'own' };

/** Parámetros de las rutas `network/**`: los del listado propio más el filtro por hijo (#12358). */
export interface NetworkChildFilter {
  /** El servidor lo INTERSECTA con el alcance del JWT: un id ajeno a la red devuelve cero filas. */
  childTenantId?: string;
}

/**
 * Lee la preferencia tal como llega del servidor (`value` es `{}` sin preferencia guardada, o lo
 * que un cliente viejo dejó). Cualquier cosa que no sea exactamente `{ mode, childTenantId? }`
 * válida cae al default: una preferencia corrupta no puede abrir un alcance que el usuario no pidió.
 */
export function parseNetworkScopePreference(raw: unknown): NetworkScopePreference {
  if (!raw || typeof raw !== 'object') return DEFAULT_NETWORK_SCOPE;
  const { mode, childTenantId } = raw as { mode?: unknown; childTenantId?: unknown };
  if (mode !== 'network') return DEFAULT_NETWORK_SCOPE;
  const child = typeof childTenantId === 'string' ? childTenantId.trim() : '';
  return child ? { mode: 'network', childTenantId: child } : { mode: 'network' };
}

/** Valor del `<select>` del selector: `own` | `network` | `child:<tenantId>`. */
export type NetworkScopeOptionValue = 'own' | 'network' | `child:${string}`;

export function scopeToOptionValue(scope: NetworkScopePreference): NetworkScopeOptionValue {
  if (scope.mode !== 'network') return 'own';
  return scope.childTenantId ? `child:${scope.childTenantId}` : 'network';
}

export function optionValueToScope(value: string): NetworkScopePreference {
  if (value === 'network') return { mode: 'network' };
  if (value.startsWith('child:')) {
    const id = value.slice('child:'.length).trim();
    return id ? { mode: 'network', childTenantId: id } : { mode: 'network' };
  }
  return DEFAULT_NETWORK_SCOPE;
}

/** Rótulos únicos del selector (los reutiliza #12364 en analítica y reportes). */
export const ETIQUETA_ALCANCE = 'Alcance';
export const ETIQUETA_ALCANCE_PROPIO = 'Mi compañía';
export const ETIQUETA_ALCANCE_RED = 'Toda la red';
export const ETIQUETA_GRUPO_HIJOS = 'Clientes de la red';
/** Texto de la celda «Cliente» para una fila propia dentro del alcance de red (AC3). */
export const ETIQUETA_CLIENTE_PROPIO = 'Mi compañía';
export const ETIQUETA_CLIENTE_HIJO = 'Cliente de la red';

// ── HU #12364 — alcance de red en estadísticas y reportes ───────────────────────────────────────

/** Distintivo de cada indicador/reporte calculado sobre la red (AC1): texto, nunca solo color. */
export const ETIQUETA_DISTINTIVO_RED = 'Red';
/** Copy de lo que en alcance de red no existe (exportaciones analíticas, pestañas sin ruta de red). */
export const COPY_SOLO_COMPANIA_PROPIA = 'Disponible solo para tu compañía';
export const COPY_CAMBIA_A_COMPANIA_PROPIA =
  'Cambia el alcance a «Mi compañía» para usar esta opción.';
/** Rótulo de lo que sigue siendo del cliente propio aunque el alcance sea la red (biometría). */
export const ETIQUETA_SOLO_COMPANIA_PROPIA = 'Solo mi compañía';

/**
 * Nombre legible del alcance de red vigente: «Toda la red» o el nombre del hijo elegido. Si el
 * hijo no está en la lista (lista no disponible), se muestra su id acortado para no ocultarlo.
 */
export function describirAlcanceRed(
  scope: NetworkScopePreference,
  hijos: ReadonlyArray<{ id: string; nombre: string }>,
): string {
  if (scope.mode !== 'network') return ETIQUETA_ALCANCE_PROPIO;
  if (!scope.childTenantId) return ETIQUETA_ALCANCE_RED;
  const hijo = hijos.find((h) => h.id === scope.childTenantId);
  return hijo ? hijo.nombre : `${ETIQUETA_CLIENTE_HIJO} ${scope.childTenantId.slice(0, 8)}`;
}
