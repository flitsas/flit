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
  FirmaBaulActorCoberturaDto,
  ProcedureActor,
} from '@/lib/api/types/procedure-runtime';

/** Redondeo a 2 decimales (precisión del contrato: `numeric(5,2)`). Evita basura de punto flotante. */
export function round2(value: number): number {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

/** Máximo de propietarios por lado (ADR-0053). */
export const MAX_OWNERS_PER_SIDE = 4;

/**
 * Rango válido por propietario cuando el lado tiene 2+ (HU #14192).
 * El mínimo es 0.01 para cumplir `numeric(5,2)` y la regla de negocio «mayor a 0».
 */
export const MIN_OWNERSHIP_SHARE = 0.01;
export const MAX_OWNERSHIP_SHARE = 100;

/** Mensajes de bloqueo — textuales, no paráfrasis (encargo cerrado + ADR-0053 notas para agentes). */
export const OWNERSHIP_SUM_MESSAGE = 'La suma de los porcentajes debe ser exactamente 100%.';
export const OWNERSHIP_ZERO_MESSAGE =
  'Todos los propietarios deben tener un porcentaje mayor a 0%.';
export const OWNERSHIP_RANGE_MESSAGE =
  'Cada propietario debe tener un porcentaje mayor a 0% y como máximo 100%.';

/** Guía en vivo (HU #14192 — edición manual con tope 100%). */
export const OWNERSHIP_CAP_MESSAGE =
  'No puedes superar el 100% entre todos los propietarios.';
export const OWNERSHIP_EXACT_MESSAGE = 'Exactamente 100%.';

/** Tolerancia de comparación de punto flotante — la regla de negocio exige EXACTAMENTE 100.00. */
const FLOAT_EPSILON = 0.005;

/** Acota un porcentaje al rango válido (0.01 .. 100). */
export function clampOwnershipShare(value: number): number {
  if (!Number.isFinite(value)) return MIN_OWNERSHIP_SHARE;
  return round2(Math.min(MAX_OWNERSHIP_SHARE, Math.max(MIN_OWNERSHIP_SHARE, value)));
}

/** Suma de porcentajes de un lado (rol). */
export function sumSharesForRol(actors: ProcedureActor[], rol: ActorRol): number {
  return round2(
    indicesForRol(actors, rol).reduce((s, i) => s + (actors[i]?.porcentaje ?? 0), 0),
  );
}

/**
 * Residuo libre del lado: `max(0, 100 − suma actual)`. Usado al agregar un copropietario
 * (nuevo % = residuo; si ya suman 100, entra en 0% y el gestor debe bajar otro antes).
 */
export function remainingShareForRol(actors: ProcedureActor[], rol: ActorRol): number {
  return round2(Math.max(0, 100 - sumSharesForRol(actors, rol)));
}

/**
 * Tope editable de un actor: `100 − suma de los demás del mismo lado`.
 * Garantiza que la suma del lado nunca supere 100 al editar (HU #14192).
 */
export function maxEditableShare(actors: ProcedureActor[], index: number): number {
  const rol = actors[index]?.rol;
  if (!rol) return MAX_OWNERSHIP_SHARE;
  const idxs = indicesForRol(actors, rol);
  const sumOthers = idxs
    .filter((i) => i !== index)
    .reduce((s, i) => s + (actors[i]?.porcentaje ?? 0), 0);
  return round2(Math.max(0, Math.min(MAX_OWNERSHIP_SHARE, 100 - sumOthers)));
}

/**
 * Acota un valor tecleado/arrastrado al rango [0, maxAllowed] (permite 0 temporal mientras se edita;
 * el guardado sigue exigiendo > 0 vía `validateOwnershipShares`).
 */
export function clampEditableShare(value: number, maxAllowed: number): number {
  const max = Math.max(0, maxAllowed);
  if (!Number.isFinite(value)) return 0;
  return round2(Math.min(max, Math.max(0, value)));
}

/**
 * Mensaje de guía según el total del lado (siempre visible con 2+ propietarios).
 * Si algún propietario está en ≤0%, no se declara "exacto" aunque la suma sea 100
 * (p. ej. 100% + 0% tras agregar).
 */
export function ownershipShareGuidance(
  total: number,
  opts?: { hasZero?: boolean; activeMaxIsZero?: boolean },
): {
  kind: 'exacto' | 'faltan' | 'superan';
  text: string;
  delta: number;
} {
  if (opts?.activeMaxIsZero) {
    return {
      kind: 'faltan',
      text: 'Baja el porcentaje de otro propietario para liberar espacio y poder asignar a este.',
      delta: 0,
    };
  }
  if (opts?.hasZero) {
    return {
      kind: 'faltan',
      text: 'Asigna un porcentaje mayor a 0% a cada propietario y completa exactamente 100%.',
      delta: round2(Math.abs(100 - round2(total))),
    };
  }
  const t = round2(total);
  const delta = round2(Math.abs(100 - t));
  if (Math.abs(t - 100) <= FLOAT_EPSILON) {
    return { kind: 'exacto', text: OWNERSHIP_EXACT_MESSAGE, delta: 0 };
  }
  if (t > 100) {
    return {
      kind: 'superan',
      text: `${OWNERSHIP_CAP_MESSAGE} (sobran ${formatGuidanceDelta(delta)}%).`,
      delta,
    };
  }
  return {
    kind: 'faltan',
    text: `Faltan ${formatGuidanceDelta(delta)}% para completar el 100%.`,
    delta,
  };
}

function formatGuidanceDelta(value: number): string {
  const rounded = round2(value);
  return Number.isInteger(rounded)
    ? String(rounded)
    : rounded.toFixed(2).replace(/0+$/, '').replace(/\.$/, '');
}

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

/** % inicial del nuevo copropietario = residuo libre del lado (nunca negativo, máx 100). */
export function percentageForNewOwner(remaining: number): number {
  if (!Number.isFinite(remaining) || remaining <= 0) return 0;
  return round2(Math.min(MAX_OWNERSHIP_SHARE, remaining));
}

/**
 * Alias de `percentageForNewOwner` — el argumento es el residuo libre, no el conteo de actores.
 */
export function defaultPercentageForNewActor(remaining: number): number {
  return percentageForNewOwner(remaining);
}

/**
 * Reparte 100% en partes iguales entre todos los actores del `rol` (último absorbe redondeo).
 * Usado al agregar un copropietario para garantizar suma exacta 100 aunque el solidario estuviera
 * marcado como editado a mano.
 */
export function equalizeSharesForRol(
  actors: ProcedureActor[],
  rol: ActorRol,
): ProcedureActor[] {
  const idxs = indicesForRol(actors, rol);
  if (idxs.length === 0) return actors;
  const next = actors.slice();
  return assignEqualShares(next, idxs);
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
    let sumOthers = restIdx.reduce((s, i) => s + (next[i]?.porcentaje ?? 0), 0);
    // HU #14192 — si los agregados dejan al solidario en ≤0 o >100, escala los demás para
    // dejarle al menos MIN_OWNERSHIP_SHARE (y como máximo MAX), manteniendo suma 100.
    const maxOthers = round2(100 - MIN_OWNERSHIP_SHARE);
    if (sumOthers > maxOthers && sumOthers > 0) {
      const scale = maxOthers / sumOthers;
      for (const i of restIdx) {
        const scaled = clampOwnershipShare((next[i]?.porcentaje ?? 0) * scale);
        if (next[i]?.porcentaje !== scaled) {
          next[i] = { ...next[i], porcentaje: scaled };
          changed = true;
        }
      }
      sumOthers = restIdx.reduce((s, i) => s + (next[i]?.porcentaje ?? 0), 0);
    }
    const residue = clampOwnershipShare(100 - sumOthers);
    // Ajuste de redondeo: el solidario absorbe el residuo exacto a 2 decimales.
    const residueExact = round2(100 - restIdx.reduce((s, i) => s + (next[i]?.porcentaje ?? 0), 0));
    const finalResidue =
      residueExact >= MIN_OWNERSHIP_SHARE && residueExact <= MAX_OWNERSHIP_SHARE
        ? residueExact
        : residue;
    if (next[firstIdx]?.porcentaje !== finalResidue) {
      next[firstIdx] = { ...next[firstIdx], porcentaje: finalResidue };
      changed = true;
    }
  }
  return changed ? next : actors;
}

/**
 * Al quitar un copropietario (HU #14192 — edición manual): deja el residuo libre en el lado
 * (no redistribuye entre los que quedan). Con un solo actor restante, éste queda con 100%
 * ESCRITO (encargo cerrado). Pura: devuelve un array NUEVO ya sin el actor eliminado.
 */
export function redistributeAfterRemoval(
  actors: ProcedureActor[],
  removedIndex: number,
): ProcedureActor[] {
  const removed = actors[removedIndex];
  if (!removed) return actors.slice();
  const rol = removed.rol;
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

  // 2+ remanentes: conservar sus % tal cual (el gestor completa el 100% a mano).
  return remaining;
}

/** Reparto equitativo 100/n entre índices del mismo lado (post-add, post-delete o descuadre). */
function assignEqualShares(
  remaining: ProcedureActor[],
  idxs: number[],
): ProcedureActor[] {
  const n = idxs.length;
  if (n === 0) return remaining;
  const equal = round2(100 / n);
  for (let k = 0; k < n; k++) {
    const i = idxs[k];
    // El último absorbe el residuo de redondeo para cerrar exactamente 100.
    const pct = k === n - 1 ? round2(100 - equal * (n - 1)) : equal;
    remaining[i] = { ...remaining[i], porcentaje: clampOwnershipShare(pct) };
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
  /** true si algún actor de un lado con 2+ actores quedó en > 100% (HU #14192). */
  rangeError: boolean;
}

/**
 * Validación de UX del reparto (espejo de `EffectiveShareValidator`, backend — §1/§6 del diseño).
 * NO reemplaza la validación autoritativa del backend: solo evita un viaje de red con datos que ya
 * se sabe que el backend va a rechazar, y da al gestor el mensaje exacto antes de intentarlo.
 * HU #14192: también rechaza porcentajes > 100.
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
  let rangeError = false;
  for (const list of byRol.values()) {
    if (list.length < 2) continue;
    const sum = list.reduce((s, a) => s + (a.porcentaje ?? 0), 0);
    if (Math.abs(sum - 100) > FLOAT_EPSILON) sumError = true;
    if (list.some((a) => (a.porcentaje ?? 0) <= 0)) zeroError = true;
    if (list.some((a) => (a.porcentaje ?? 0) > MAX_OWNERSHIP_SHARE)) rangeError = true;
  }
  return { valid: !sumError && !zeroError && !rangeError, sumError, zeroError, rangeError };
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
// `FirmaFurStep.tsx` (resumen previo a radicar) necesita mostrar, por CADA copropietario, si ya
// validó su identidad — "el gestor tiene que poder ver a quién le falta" (no solo un agregado por
// lado que puede decir "falta 1" con 3 pendientes).
//
// Correlación por ORDINAL, no por documento — y a propósito: `documentNumber` en
// `BiometricValidationDto` y en `FirmaBaulActorCoberturaDto` es el documento del SUJETO de
// identidad, que para persona JURÍDICA es el representante legal, no el NIT de la compañía
// (`actor.numeroDocumento`). Comparar contra el NIT ahí daría un falso negativo permanente para
// cualquier actor jurídico. `ordinal` (ADR-0053, en ambos DTOs) evita esa ambigüedad por completo:
// identifica la fila sin necesidad de saber cuál documento es "el" del actor.
//
// Con `ordinal` ausente (`null`) en una fila — validación histórica/huérfana previa a ADR-0053 — se
// cae a comparar por documento como antes (correcto para persona natural, que es el caso de esas
// filas viejas: la firma del baúl no existía como concepto "por actor" hasta este cierre).
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
  manual_activo: { label: 'Esperando captura del cliente', tone: 'info' },
  pendiente_revision_manual: { label: 'Pendiente de revisión', tone: 'info' },
};

/**
 * Estado de identidad/firma de UN actor (identificado por `rol` + `ordinal`), para pintar en las
 * pantallas de solo lectura. Prioridad: 1) validación biométrica propia (correlacionada por
 * `ordinal`, con fallback a documento para filas históricas sin `ordinal`), 2) cobertura de firma
 * del baúl DE ESE ACTOR (`firmaBaulActores`, ADR-0053 — dato real, ya no aproximado por lado),
 * 3) "Pendiente" (nadie ha hecho nada por esta persona todavía — el caso por defecto, nunca un
 * falso "aprobado").
 */
export function identityStatusForActor(
  actor: Pick<ProcedureActor, 'rol' | 'numeroDocumento'>,
  ordinal: number,
  biometric: readonly Pick<BiometricValidation, 'documentNumber' | 'partyRole' | 'status' | 'ordinal'>[],
  firmaBaulActores: readonly Pick<FirmaBaulActorCoberturaDto, 'parte' | 'ordinal'>[] = [],
): ActorIdentityStatus {
  const bio = biometric.find((b) => biometricValidationMatchesActor(b, actor, ordinal));
  if (bio) {
    return BIOMETRIC_ESTADO_STATUS[bio.status] ?? { label: bio.status, tone: 'neutral' };
  }
  if (isCoveredByVaultForActor(firmaBaulActores, actor.rol, ordinal)) {
    return { label: 'Firma del baúl', tone: 'info' };
  }
  return { label: 'Pendiente', tone: 'warning' };
}

/**
 * Predicado de correlación actor↔validación biométrica — la pieza compartida entre
 * `identityStatusForActor` (solo el estado/tono) y `BiometricStep.tsx` (necesita la fila COMPLETA:
 * score, captureUrl, intentos, certificado…, no solo un label). Un solo lugar donde vive la regla
 * de correlación evita que las dos pantallas de solo lectura terminen divergiendo.
 *
 * Prioridad: `ordinal` (ADR-0053, evita comparar documentos — para persona jurídica el documento
 * del sujeto de identidad es el del representante legal, no el NIT del actor); fallback por
 * documento SOLO para filas históricas sin `ordinal` (anteriores a ADR-0053).
 */
export function biometricValidationMatchesActor(
  validation: Pick<BiometricValidation, 'documentNumber' | 'partyRole' | 'ordinal'>,
  actor: Pick<ProcedureActor, 'rol' | 'numeroDocumento'>,
  ordinal: number,
): boolean {
  const mismaParte = validation.partyRole === actor.rol || validation.partyRole === null;
  if (!mismaParte) return false;
  if (validation.ordinal != null) return validation.ordinal === ordinal;
  const numero = actor.numeroDocumento.trim();
  return numero ? validation.documentNumber.trim() === numero : false;
}

/**
 * TODAS las validaciones (historial completo, no solo la vigente) que corresponden a UN actor
 * específico — `BiometricStep.tsx` las necesita para su propio "Historial de validaciones (N)" por
 * actor, no por lado. Mismo predicado que `identityStatusForActor`, sin filtrar por estado.
 */
export function validationsForActor<T extends Pick<BiometricValidation, 'documentNumber' | 'partyRole' | 'ordinal'>>(
  validations: readonly T[],
  actor: Pick<ProcedureActor, 'rol' | 'numeroDocumento'>,
  ordinal: number,
): T[] {
  return validations.filter((v) => biometricValidationMatchesActor(v, actor, ordinal));
}

/** Cobertura del baúl de UN actor específico (ordinal dentro de su rol) — nunca por lado. */
export function isCoveredByVaultForActor(
  firmaBaulActores: readonly Pick<FirmaBaulActorCoberturaDto, 'parte' | 'ordinal'>[],
  rol: ActorRol,
  ordinal: number,
): boolean {
  return firmaBaulActores.some((c) => c.parte === rol && c.ordinal === ordinal);
}
