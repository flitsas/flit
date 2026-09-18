import type {
  BiometricEstado,
  BiometricVigenciaEstado,
  TenantBiometricPersonFilters,
} from '@/lib/api/types/procedure-runtime';
import { digitsOnly } from '@/lib/format/currency';
import { SEARCH_TEXT_MAX_LENGTH, sanitizeNoAngleBrackets } from '@/lib/validation/fieldRules';

/**
 * HU #12707 — filtros de Validación de Identidad con la barra de Trámites (buscador, Periodo y
 * «+ Filtro» con chips). Lógica pura, sin React: qué se puede filtrar, cómo se describe cada filtro en
 * su chip, cuándo un valor es inválido (AC10) y cómo se traduce a los MISMOS parámetros de API que usaba
 * el panel anterior (AC2) — `name`/`documentNumber`, `status`, `vigenciaEstado`, `createdFrom`/`createdTo`,
 * `expiraDesde`/`expiraHasta`, `venceEnDias` y `standalone`.
 */

/** Filtros efectivamente aplicados (forma plana; es lo que se traduce a la API). */
export interface ValidacionesUiFilters {
  name: string;
  documentNumber: string;
  status: '' | BiometricEstado;
  vigenciaEstado: '' | BiometricVigenciaEstado;
  /** `yyyy-mm-dd` local. */
  createdFrom: string;
  createdTo: string;
  expiraDesde: string;
  expiraHasta: string;
  venceEnDias: string;
  /** `''` = todas; `'prevalidacion'` ⇒ `standalone=true`; `'tramite'` ⇒ `standalone=false`. */
  origen: '' | 'prevalidacion' | 'tramite';
}

export const EMPTY_VALIDACIONES_FILTERS: ValidacionesUiFilters = {
  name: '',
  documentNumber: '',
  status: '',
  vigenciaEstado: '',
  createdFrom: '',
  createdTo: '',
  expiraDesde: '',
  expiraHasta: '',
  venceEnDias: '',
  origen: '',
};

export function hasActiveValidacionesFilters(f: ValidacionesUiFilters): boolean {
  return (
    f.name.trim() !== '' ||
    f.documentNumber.trim() !== '' ||
    f.status !== '' ||
    f.vigenciaEstado !== '' ||
    f.createdFrom !== '' ||
    f.createdTo !== '' ||
    f.expiraDesde !== '' ||
    f.expiraHasta !== '' ||
    f.venceEnDias.trim() !== '' ||
    f.origen !== ''
  );
}

/**
 * Una sola caja «Nombre o número de documento»: si lo escrito es casi todo dígitos (≥ 4 y ≥ 80 %) se
 * busca como documento, si no como nombre. Misma regla que el panel anterior.
 */
export function splitPersonaODocumentoQuery(
  q: string,
): Pick<ValidacionesUiFilters, 'name' | 'documentNumber'> {
  const t = q.trim();
  if (!t) return { name: '', documentNumber: '' };
  const compact = t.replace(/\s/g, '');
  const digits = digitsOnly(t);
  if (digits.length >= 4 && digits.length >= compact.length * 0.8) {
    return { name: '', documentNumber: digits };
  }
  return { name: sanitizeNoAngleBrackets(t).slice(0, SEARCH_TEXT_MAX_LENGTH), documentNumber: '' };
}

// ── «+ Filtro» ─────────────────────────────────────────────────────────────────────────────────

export type IdentityFilterFieldId = 'status' | 'vigencia' | 'venceEntre' | 'venceEnDias' | 'origen';

export type IdentityFilterKind = 'opcion' | 'rango-fecha' | 'numero';

export interface IdentityFilterOption {
  value: string;
  label: string;
}

export interface IdentityFilterField {
  id: IdentityFilterFieldId;
  label: string;
  kind: IdentityFilterKind;
  /** Solo para `opcion`: una sola elección (la API recibe un valor, no una lista). */
  options: IdentityFilterOption[];
  hint?: string;
}

/**
 * Una condición aplicada. `values` según el tipo: `opcion` ⇒ `[valor]`; `rango-fecha` ⇒ `[desde, hasta]`
 * (`yyyy-mm-dd`, cualquiera puede ir vacío pero no los dos); `numero` ⇒ `[días]`.
 */
export interface IdentityFilterCondition {
  fieldId: IdentityFilterFieldId;
  values: string[];
}

export const ESTADO_OPTIONS: IdentityFilterOption[] = [
  { value: 'enviado', label: 'Enviado' },
  { value: 'en_proceso', label: 'En proceso' },
  { value: 'aprobado', label: 'Aprobado' },
  { value: 'rechazado', label: 'Rechazado' },
  { value: 'expirado', label: 'Expirado' },
  { value: 'pendiente_envio', label: 'Pendiente de envío' },
  { value: 'error_envio', label: 'Error de envío' },
];

export const VIGENCIA_OPTIONS: IdentityFilterOption[] = [
  { value: 'vigente', label: 'Vigente' },
  { value: 'por_vencer', label: 'Por vencer (≤7 días)' },
  { value: 'vencida', label: 'Vencida' },
];

export const ORIGEN_OPTIONS: IdentityFilterOption[] = [
  { value: 'prevalidacion', label: 'Prevalidación' },
  { value: 'tramite', label: 'Trámite' },
];

/** Catálogo de «+ Filtro», en el orden en que se ofrece. */
export const IDENTITY_FILTER_FIELDS: IdentityFilterField[] = [
  { id: 'status', label: 'Estado', kind: 'opcion', options: ESTADO_OPTIONS },
  { id: 'vigencia', label: 'Vigencia', kind: 'opcion', options: VIGENCIA_OPTIONS },
  {
    id: 'venceEntre',
    label: 'Vence entre…',
    kind: 'rango-fecha',
    options: [],
    hint: 'Fin de la vigencia de la identidad aprobada (30 días desde la aprobación).',
  },
  {
    id: 'venceEnDias',
    label: 'Vence en ≤ N días',
    kind: 'numero',
    options: [],
    hint: 'Identidades aprobadas y vigentes que vencen en ese número de días o menos.',
  },
  { id: 'origen', label: 'Origen', kind: 'opcion', options: ORIGEN_OPTIONS },
];

export function identityFilterField(id: IdentityFilterFieldId): IdentityFilterField {
  const field = IDENTITY_FILTER_FIELDS.find((f) => f.id === id);
  if (!field) throw new Error(`Filtro de identidad desconocido: ${id}`);
  return field;
}

/** Tope del filtro «Vence en ≤ N días»: la vigencia dura 30 días, pedir más no cambia el resultado. */
export const VENCE_EN_DIAS_MAX = 365;

/**
 * AC10 — motivo por el que un valor no se puede aplicar, o `null` si es válido. Un filtro inválido no
 * se aplica ni dispara petición: el editor muestra este texto.
 */
export function validateIdentityFilter(fieldId: IdentityFilterFieldId, values: string[]): string | null {
  const field = identityFilterField(fieldId);
  switch (field.kind) {
    case 'opcion': {
      const value = values[0] ?? '';
      return field.options.some((o) => o.value === value) ? null : 'Elige una opción.';
    }
    case 'numero': {
      const raw = (values[0] ?? '').trim();
      if (raw === '') return 'Escribe un número de días.';
      if (!/^\d+$/.test(raw)) return 'Escribe solo números enteros (por ejemplo, 7).';
      const n = Number(raw);
      if (n < 0 || n > VENCE_EN_DIAS_MAX) return `Escribe un número entre 0 y ${VENCE_EN_DIAS_MAX}.`;
      return null;
    }
    case 'rango-fecha': {
      const [desde = '', hasta = ''] = values;
      if (desde === '' && hasta === '') return 'Elige al menos una fecha.';
      if (desde !== '' && hasta !== '' && hasta < desde) {
        return 'La fecha final no puede ser anterior a la inicial.';
      }
      return null;
    }
  }
}

const formatDia = (iso: string) => {
  const [y, m, d] = iso.split('-');
  return y && m && d ? `${d}/${m}/${y}` : iso;
};

/** Texto del chip de una condición aplicada (p. ej. «Estado: Aprobado», «Vence en ≤ 7 días»). */
export function describeIdentityFilter(condition: IdentityFilterCondition): string {
  const field = identityFilterField(condition.fieldId);
  switch (field.kind) {
    case 'opcion': {
      const label = field.options.find((o) => o.value === condition.values[0])?.label ?? condition.values[0];
      return `${field.label}: ${label}`;
    }
    case 'numero':
      return `Vence en ≤ ${condition.values[0]} día${condition.values[0] === '1' ? '' : 's'}`;
    case 'rango-fecha': {
      const [desde = '', hasta = ''] = condition.values;
      if (desde && hasta) return `Vence entre ${formatDia(desde)} y ${formatDia(hasta)}`;
      return desde ? `Vence desde ${formatDia(desde)}` : `Vence hasta ${formatDia(hasta)}`;
    }
  }
}

/**
 * Lleva buscador + periodo + condiciones a la forma plana aplicada. `periodo` es el rango YA resuelto
 * (`rangoDePeriodo` de Trámites o el rango propio), en fechas locales.
 */
export function toUiFilters(
  search: string,
  periodo: { desde: string; hasta: string } | null,
  condiciones: readonly IdentityFilterCondition[],
): ValidacionesUiFilters {
  const f: ValidacionesUiFilters = {
    ...EMPTY_VALIDACIONES_FILTERS,
    ...splitPersonaODocumentoQuery(search),
    createdFrom: periodo?.desde ?? '',
    createdTo: periodo?.hasta ?? '',
  };
  for (const c of condiciones) {
    switch (c.fieldId) {
      case 'status':
        f.status = (c.values[0] ?? '') as ValidacionesUiFilters['status'];
        break;
      case 'vigencia':
        f.vigenciaEstado = (c.values[0] ?? '') as ValidacionesUiFilters['vigenciaEstado'];
        break;
      case 'venceEntre':
        f.expiraDesde = c.values[0] ?? '';
        f.expiraHasta = c.values[1] ?? '';
        break;
      case 'venceEnDias':
        f.venceEnDias = c.values[0] ?? '';
        break;
      case 'origen':
        f.origen = (c.values[0] ?? '') as ValidacionesUiFilters['origen'];
        break;
    }
  }
  return f;
}

/**
 * AC2 — filtros aplicados a parámetros de `GET /biometric-validations/by-person`. Vacíos ⇒ `undefined`
 * (no se envían); fechas con `T00:00:00` / `T23:59:59` para incluir el día elegido, igual que antes.
 */
export function buildPersonApiFilters(f: ValidacionesUiFilters): TenantBiometricPersonFilters {
  const text = (s: string) => (s.trim() === '' ? undefined : s.trim());
  const num = (s: string) => {
    if (s.trim() === '') return undefined;
    const n = Number(s);
    return Number.isNaN(n) ? undefined : n;
  };
  return {
    name: text(f.name),
    documentNumber: text(f.documentNumber),
    status: f.status || undefined,
    createdFrom: f.createdFrom ? `${f.createdFrom}T00:00:00` : undefined,
    createdTo: f.createdTo ? `${f.createdTo}T23:59:59` : undefined,
    vigenciaEstado: f.vigenciaEstado || undefined,
    expiraDesde: f.expiraDesde ? `${f.expiraDesde}T00:00:00` : undefined,
    expiraHasta: f.expiraHasta ? `${f.expiraHasta}T23:59:59` : undefined,
    venceEnDias: num(f.venceEnDias),
    // Sin «Origen» la clave NO viaja (HU #11006, CF-03): el listado mezcla prevalidaciones y trámites.
    ...(f.origen === '' ? {} : { standalone: f.origen === 'prevalidacion' }),
  };
}
