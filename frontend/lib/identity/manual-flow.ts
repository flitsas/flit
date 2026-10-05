import type { StatusTone } from '@flit/ui/StatusBadge';
import type { BiometricEstado, BiometricValidation } from '@/lib/api/types/procedure-runtime';

/**
 * HU #13288 (Épica #13202) — vocabulario y reglas del flujo manual de identidad en el detalle de una
 * validación. Fuente única del mapeo de los estados nuevos del backend (`manual_activo` y
 * `pendiente_revision_manual`): los demás módulos lo extienden desde aquí en vez de repetir los textos.
 */

export const MANUAL_ESTADO_META: Record<
  'manual_activo' | 'pendiente_revision_manual',
  { label: string; tone: StatusTone }
> = {
  manual_activo: { label: 'Esperando captura del cliente', tone: 'info' },
  pendiente_revision_manual: { label: 'Pendiente de revisión', tone: 'info' },
};

/** Vigencia de una identidad aprobada (espejo de `BiometricRules.VigenciaDias`). */
const VIGENCIA_DIAS = 30;
const MS_DIA = 24 * 60 * 60 * 1000;

/**
 * ¿Aprobada y vigente? El detalle no recibe `validUntil`, así que se calcula como el backend cuando
 * falta ese campo: `validatedAt + 30 días`. Sin `validatedAt` se asume vigente (lado seguro: no se
 * ofrece activar el flujo manual). El backend responde 409 si discrepa.
 */
export function esAprobadaVigente(v: Pick<BiometricValidation, 'status' | 'validatedAt'>, now = Date.now()): boolean {
  if (v.status !== 'aprobado') return false;
  if (!v.validatedAt) return true;
  const t = Date.parse(v.validatedAt);
  if (Number.isNaN(t)) return true;
  return now < t + VIGENCIA_DIAS * MS_DIA;
}

const KYVERUM_EN_CURSO: BiometricEstado[] = ['enviado', 'en_proceso', 'pendiente_envio'];

/** ¿Hay una verificación de Kyverum en curso que la activación manual cancelaría? */
export function hayKyverumEnCurso(v: Pick<BiometricValidation, 'provider' | 'status'>): boolean {
  return v.provider === 'kyverum' && KYVERUM_EN_CURSO.includes(v.status);
}

export function esFlujoManualActivo(v: Pick<BiometricValidation, 'provider' | 'status'>): boolean {
  return v.provider === 'manual' && v.status === 'manual_activo';
}

export type AccionManual = 'activar' | 'regenerar';

/** Mensaje claro para el Super Admin según el status HTTP y el código (`title` del ProblemDetails). */
export function mensajeErrorFlujoManual(err: unknown, accion: AccionManual): string {
  const { status, problem } = (err ?? {}) as { status?: unknown; problem?: unknown };
  const title =
    problem && typeof problem === 'object' && typeof (problem as { title?: unknown }).title === 'string'
      ? (problem as { title: string }).title
      : '';
  if (status === 409) {
    switch (title) {
      case 'identidad_aprobada_vigente':
        return 'Esta validación ya está aprobada y vigente.';
      case 'identidad_manual':
        return 'Esta validación ya está en flujo manual.';
      case 'tramite_inactivo':
        return 'El trámite está anulado o revocado.';
      case 'flujo_manual_no_activo':
        return 'El flujo manual ya no está activo en esta validación. Actualiza el estado.';
      default:
        return 'La validación cambió de estado. Actualiza el detalle e inténtalo de nuevo.';
    }
  }
  if (status === 403) return 'No tienes permiso para esta acción. Solo el Super Admin puede hacerla.';
  if (status === 404) return 'No se encontró la validación. Actualiza el detalle.';
  return accion === 'activar'
    ? 'No se pudo activar el flujo manual. Inténtalo de nuevo.'
    : 'No se pudo regenerar el enlace. Inténtalo de nuevo.';
}
