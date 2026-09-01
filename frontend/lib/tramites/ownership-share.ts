// Múltiple Propietario (ADR-0053, docs/design/MULTIPLE-PROPIETARIO-diseno-tecnico.md §4.5, §6).
//
// Lógica PURA del reparto porcentual entre copropietarios de un mismo lado (rol). El backend es
// SIEMPRE la autoridad sobre el resultado (suma == 100.00, ninguno en 0) — este módulo solo existe
// para dar una buena experiencia de edición en vivo mientras el gestor arrastra el slider o teclea
// la casilla decimal. Nada aquí persiste; `ActorsForm.tsx` es quien decide cuándo aplicar el
// resultado al estado de React.
//
// La "regla del solidario" (§4.5) — el ordinal=1 de cada lado absorbe el residuo (100 − suma de
// los demás) mientras el gestor no edite su propio porcentaje a mano — vive AQUÍ, exclusivamente en
// frontend, por diseño: el backend nunca sabe ni necesita saber si el gestor ya tocó el % del
// principal, solo recibe el conjunto final.

import type {
  ActorRol,
  BiometricEstado,
  BiometricValidation,
  ProcedureActor,
} from '@/lib/api/types/procedure-runtime';

/** Redondeo a 2 decimales (precisión del contrato: `numeric(5,2)`). Evita basura de punto flotante. */
export function round2(value: number): number {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

/** Máximo de propietarios por lado (ADR-0053). */
export const MAX_OWNERS_PER_SIDE = 4;

/** Mensajes de bloqueo — textuales, no paráfrasis (encargo cerrado + ADR-0053 notas para agentes). */
export const OWNERSHIP_SUM_MESSAGE = 'La suma de los porcentajes debe ser exactamente 100%.';
export const OWNERSHIP_ZERO_MESSAGE =
  'Todos los propietarios deben tener un porcentaje mayor a 0%.';

/**
 * Ordena por `ordinal` (ausente ⇒ 1) cualquier lista de actores/entidades que lo traigan — usada
 * por las pantallas de SOLO LECTURA (`TramiteDetalleActores.tsx`, `FirmaFurStep.tsx`) para pintar
 * los copropietarios de un lado en su orden real, sin asumir que la respuesta del backend ya viene
 * ordenada. Genérica a propósito: no depende de `ProcedureActor` completo, solo de `ordinal`.
 */
export function actorsOrderedByOrdinal<T extends { ordinal?: number }>(
  items: T[],
): { item: T; ordinal: number }[] {
  return items
    .map((item) => ({ item, ordinal: item.ordinal ?? 1 }))
    .sort((a, b) => a.ordinal - b.ordinal);
}

/** Índices (posición en el array `actors`) que comparten un mismo `rol`, en el orden en que aparecen. */
export function indicesForRol(actors: ProcedureActor[], rol: ActorRol): number[] {
  const out: number[] = [];
  actors.forEach((a, i) => {
    if (a.rol === rol) out.push(i);
  });
  return out;
}

/** ¿Este índice es el actor ordinal=1 (principal/solidario) de su lado? Primer índice del grupo. */
export function isFirstOfRol(actors: ProcedureActor[], index: number): boolean {
  const rol = actors[index]?.rol;
  if (!rol) return false;
  return indicesForRol(actors, rol)[0] === index;
}

/**
 * Porcentaje por defecto de un propietario recién agregado: reparto equitativo entre TODOS los
 * actores que quedarán en el lado (incluido el que se está agregando). El diseño no fija un valor
 * exacto para este caso (decisión de UI del frontend-agent) — un reparto equitativo es el punto de
 * partida más neutral, y el solidario lo corrige de inmediato vía `applySolidarioAbsorption`.
 */
export function defaultPercentageForNewActor(countAfterAdd: number): number {
  if (countAfterAdd <= 0) return 100;
  return round2(100 / countAfterAdd);
}

/**
 * Aplica la auto-absorción del residuo del ordinal=1 de CADA lado con 2+ actores, salvo que ese
 * lado ya haya sido editado a mano (`manuallyEdited.has(rol)`). Pura: no muta `actors`, devuelve
 * `actors` SIN cambios (misma referencia) si no hay nada que absorber — así el llamador puede usar
 * el resultado directamente como valor de un `setState` funcional sin re-renderizar de más.
 */
export function applySolidarioAbsorption(
  actors: ProcedureActor[],
  manuallyEdited: ReadonlySet<ActorRol>,
): ProcedureActor[] {
  let changed = false;
  const next = actors.slice();
  const seenRoles = new Set<ActorRol>();
  for (const a of actors) seenRoles.add(a.rol);

  for (const rol of seenRoles) {
    const idxs = indicesForRol(actors, rol);
    if (idxs.length < 2 || manuallyEdited.has(rol)) continue;
    const [firstIdx, ...restIdx] = idxs;
    const sumOthers = restIdx.reduce((s, i) => s + (next[i]?.porcentaje ?? 0), 0);
    const residue = round2(100 - sumOthers);
    if (next[firstIdx]?.porcentaje !== residue) {
      next[firstIdx] = { ...next[firstIdx], porcentaje: residue };
      changed = true;
    }
  }
  return changed ? next : actors;
}

/**
 * Redistribuye el porcentaje de un actor eliminado entre los que quedan de su mismo lado,
 * proporcionalmente a su porcentaje actual (si todos están en 0, reparto equitativo). Con un solo
 * actor restante, éste queda con 100% ESCRITO (encargo cerrado: "si eliminan el segundo, el
 * primero queda con 100% escrito"). Pura: devuelve un array NUEVO ya sin el actor eliminado.
 */
export function redistributeAfterRemoval(
  actors: ProcedureActor[],
  removedIndex: number,
): ProcedureActor[] {
  const removed = actors[removedIndex];
  if (!removed) return actors.slice();
  const rol = removed.rol;
  const removedShare = removed.porcentaje ?? 0;
  const remaining = actors.filter((_, i) => i !== removedIndex);
  const remainingIdxsOfRol: number[] = [];
  remaining.forEach((a, i) => {
    if (a.rol === rol) remainingIdxsOfRol.push(i);
  });

  if (remainingIdxsOfRol.length === 0) return remaining;

  if (remainingIdxsOfRol.length === 1) {
    const only = remainingIdxsOfRol[0];
    remaining[only] = { ...remaining[only], porcentaje: 100 };
    return remaining;
  }

  const totalRemainingShare = remainingIdxsOfRol.reduce(
    (s, i) => s + (remaining[i].porcentaje ?? 0),
    0,
  );
  if (totalRemainingShare <= 0) {
    // Nadie tenía porcentaje positivo (residuo negativo, todos en 0): reparto equitativo.
    const equal = round2(100 / remainingIdxsOfRol.length);
    for (const i of remainingIdxsOfRol) remaining[i] = { ...remaining[i], porcentaje: equal };
    return remaining;
  }
  for (const i of remainingIdxsOfRol) {
    const current = remaining[i].porcentaje ?? 0;
    const extra = removedShare * (current / totalRemainingShare);
    remaining[i] = { ...remaining[i], porcentaje: round2(current + extra) };
  }
  return remaining;
}

// ── Reindexado de mapas por posición (mitigación de "estado fantasma") ───────────────────────────
//
// `ActorsForm.tsx` guarda estado efímero por actor (consulta RUNT/RUES, representante legal
// elegido, autocompletar de ciudad, etc.) en varios `Record<number, X>` indexados por la POSICIÓN
// del actor en el array `actors`, no por un id estable. Insertar o quitar un copropietario desplaza
// esa posición para todos los actores que quedan DESPUÉS del punto de inserción/eliminación — sin
// reindexar esos mapas en el mismo gesto, la consulta de un actor eliminado quedaría reasociada al
// actor equivocado (el riesgo de "estado fantasma" del encargo). Estas dos funciones son el único
// lugar donde ese reindexado ocurre; `ActorsForm.tsx` las aplica a los 11 mapas a la vez, en el
// mismo `addOwner`/`removeOwner` que muta `actors`.
export function shiftIndexMapOnInsert<T>(map: Record<number, T>, at: number): Record<number, T> {
  const next: Record<number, T> = {};
  for (const key of Object.keys(map)) {
    const k = Number(key);
    next[k >= at ? k + 1 : k] = map[k];
  }
  return next;
}

export function shiftIndexMapOnRemove<T>(map: Record<number, T>, at: number): Record<number, T> {
  const next: Record<number, T> = {};
  for (const key of Object.keys(map)) {
    const k = Number(key);
    if (k === at) continue;
    next[k > at ? k - 1 : k] = map[k];
  }
  return next;
}

export interface OwnershipShareValidation {
  valid: boolean;
  /** true si algún lado con 2+ actores no suma exactamente 100.00. */
  sumError: boolean;
  /** true si algún actor de un lado con 2+ actores quedó en <= 0%. */
  zeroError: boolean;
}

/** Tolerancia de comparación de punto flotante — la regla de negocio exige EXACTAMENTE 100.00. */
const FLOAT_EPSILON = 0.005;

/**
 * Validación de UX del reparto (espejo de `EffectiveShareValidator`, backend — §1/§6 del diseño).
 * NO reemplaza la validación autoritativa del backend: solo evita un viaje de red con datos que ya
 * se sabe que el backend va a rechazar, y da al gestor el mensaje exacto antes de intentarlo.
 */
export function validateOwnershipShares(actors: ProcedureActor[]): OwnershipShareValidation {
  const byRol = new Map<ActorRol, ProcedureActor[]>();
  for (const a of actors) {
    const list = byRol.get(a.rol) ?? [];
    list.push(a);
    byRol.set(a.rol, list);
  }
  let sumError = false;
  let zeroError = false;
  for (const list of byRol.values()) {
    if (list.length < 2) continue;
    const sum = list.reduce((s, a) => s + (a.porcentaje ?? 0), 0);
    if (Math.abs(sum - 100) > FLOAT_EPSILON) sumError = true;
    if (list.some((a) => (a.porcentaje ?? 0) <= 0)) zeroError = true;
  }
  return { valid: !sumError && !zeroError, sumError, zeroError };
}

/**
 * Detecta documentos duplicados DENTRO del mismo lado (§4.4 Nivel 1 — bloqueada SIEMPRE, sin
 * excepción). Devuelve los índices (en `actors`) de los actores duplicados (la SEGUNDA aparición en
 * adelante), para que el llamador marque el error sobre esas tarjetas/pestañas.
 */
export function duplicateDocumentIndicesWithinSide(actors: ProcedureActor[]): number[] {
  const seen = new Map<string, Set<string>>(); // rol -> set de "tipo|numero"
  const dupes: number[] = [];
  actors.forEach((a, i) => {
    const numero = a.numeroDocumento.trim();
    if (!numero) return;
    const key = `${a.tipoDocumento}|${numero}`;
    const set = seen.get(a.rol) ?? new Set<string>();
    if (set.has(key)) {
      dupes.push(i);
    } else {
      set.add(key);
      seen.set(a.rol, set);
    }
  });
  return dupes;
}

/**
 * Ordinal (1-based) de cada actor DENTRO de su lado, en el orden en que aparece en `actors`. El
 * array debe mantener contiguos a los actores de un mismo `rol` (invariante que `ActorsForm.tsx`
 * preserva: los agregados siempre se insertan al final del grupo de su lado, nunca antes del
 * ordinal=1) — con eso, "primera aparición" == ordinal=1 == principal/solidario.
 */
export function computeOrdinals(actors: ProcedureActor[]): number[] {
  const seen = new Map<ActorRol, number>();
  return actors.map((a) => {
    const next = (seen.get(a.rol) ?? 0) + 1;
    seen.set(a.rol, next);
    return next;
  });
}

/**
 * Prepara el array de actores para el PUT: agrega `ordinal` (posición 1-based dentro del lado) y
 * `porcentaje` (null si el lado quedó con un solo actor — "si viene con valor, se ignora", pero se
 * envía null explícito para no depender de esa tolerancia del backend).
 */
export function withOwnershipFields(actors: ProcedureActor[]): ProcedureActor[] {
  const countByRol = new Map<ActorRol, number>();
  for (const a of actors) countByRol.set(a.rol, (countByRol.get(a.rol) ?? 0) + 1);
  const ordinals = computeOrdinals(actors);
  return actors.map((a, i) => {
    const count = countByRol.get(a.rol) ?? 1;
    return {
      ...a,
      ordinal: ordinals[i],
      porcentaje: count > 1 ? round2(a.porcentaje ?? 0) : null,
    };
  });
}

// ── Estado de identidad/firma por actor (pantallas de solo lectura) ──────────────────────────────
//
// `FirmaFurStep.tsx` (resumen previo a radicar) y `TramiteDetalleActores.tsx` (modal de detalle)
// necesitan mostrar, por CADA copropietario, si ya validó su identidad — "el gestor tiene que poder
// ver a quién le falta" (no solo un agregado por lado que puede decir "falta 1" con 3 pendientes).
//
// Dato disponible HOY: `BiometricValidation[]` (GET .../biometric-validations, ya lo consume
// `FirmaFurStep.tsx`) trae `documentNumber` por fila — la correlación por documento que el diseño
// (ADR-0053 §1, Opción A) ya daba por sentada ("`procedure_instance_biometric_validations` YA es
// 1:N por `PartyRole`+`DocumentNumber`"). Este helper correlaciona por ESE documento, no por rol a
// secas, así que si el backend hoy solo persiste/devuelve la fila del ordinal=1 (la brecha que el
// backend-agent está cerrando en paralelo — ver nota de datos faltantes en el handoff), los
// agregados simplemente no encuentran fila y caen a "Pendiente": no hay error, pero tampoco hay
// falso positivo.
//
// Dato NO disponible por actor (aproximado por LADO): `firmaBaulPartes`/`vaultCoveredPartes` son
// `BiometricParte[]` (comprador/vendedor), no una lista de documentos — no distinguen cuál
// copropietario jurídico de un mismo lado está cubierto por el baúl. Se usa como aproximación
// best-effort (documentado en la UI) mientras el backend no exponga esa cobertura por actor.
export interface ActorIdentityStatus {
  label: string;
  tone: 'success' | 'warning' | 'danger' | 'info' | 'neutral';
}

const BIOMETRIC_ESTADO_STATUS: Record<BiometricEstado, ActorIdentityStatus> = {
  aprobado: { label: 'Identidad aprobada', tone: 'success' },
  rechazado: { label: 'Identidad rechazada', tone: 'danger' },
  expirado: { label: 'Identidad vencida', tone: 'danger' },
  en_proceso: { label: 'Validación en proceso', tone: 'info' },
  enviado: { label: 'Enlace de validación enviado', tone: 'info' },
  pendiente_envio: { label: 'Pendiente de envío', tone: 'warning' },
  error_envio: { label: 'Error al enviar la validación', tone: 'warning' },
};

/**
 * Estado de identidad/firma de UN actor, para pintar en las pantallas de solo lectura. Prioridad:
 * 1) validación biométrica propia (correlacionada por documento — ver nota arriba), 2) cobertura de
 * firma del baúl aproximada por lado (solo si es persona jurídica), 3) "Pendiente" (nadie ha hecho
 * nada por esta persona todavía — el caso por defecto, nunca un falso "aprobado").
 */
export function identityStatusForActor(
  actor: Pick<ProcedureActor, 'rol' | 'numeroDocumento' | 'personType'>,
  biometric: Pick<BiometricValidation, 'documentNumber' | 'partyRole' | 'status'>[],
  firmaBaulPartes: readonly string[] = [],
): ActorIdentityStatus {
  const numero = actor.numeroDocumento.trim();
  const bio = numero
    ? biometric.find(
        (b) =>
          b.documentNumber.trim() === numero &&
          (b.partyRole === actor.rol || b.partyRole === null),
      )
    : undefined;
  if (bio) {
    return BIOMETRIC_ESTADO_STATUS[bio.status] ?? { label: bio.status, tone: 'neutral' };
  }
  if (actor.personType === 'juridical' && firmaBaulPartes.includes(actor.rol)) {
    return { label: 'Firma del baúl', tone: 'info' };
  }
  return { label: 'Pendiente', tone: 'warning' };
}
