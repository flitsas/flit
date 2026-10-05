import type { ComponentType } from 'react';
import { CheckCircle2, Clock, Hourglass, Timer, XCircle } from 'lucide-react';
import type { StatusTone } from '@/components/atom/StatusBadge';
import type { ManualOrigin, ManualStatus } from '@/lib/api/types/manual-review';

/**
 * Presentación de la revisión manual (Épica #13202): estado → tono FLIT + icono + texto, y origen →
 * etiqueta. Solo los 5 tonos de badge de la línea base; el significado nunca depende solo del color.
 */
export interface ManualStatusMeta {
  label: string;
  tone: StatusTone;
  icon: ComponentType<{ className?: string; 'aria-hidden'?: boolean }>;
}

export const MANUAL_STATUS_META: Record<ManualStatus, ManualStatusMeta> = {
  manual_activo: { label: 'Esperando captura', tone: 'info', icon: Hourglass },
  pendiente_revision_manual: { label: 'Pendiente de revisión', tone: 'info', icon: Clock },
  aprobado: { label: 'Aprobada manual', tone: 'success', icon: CheckCircle2 },
  rechazado: { label: 'Rechazada', tone: 'danger', icon: XCircle },
  expirado: { label: 'Vencida', tone: 'neutral', icon: Timer },
};

/** Estado desconocido (el backend puede añadir uno): neutral, con el código visible. */
export function manualStatusMeta(status: string): ManualStatusMeta {
  return (
    MANUAL_STATUS_META[status as ManualStatus] ?? { label: status, tone: 'neutral', icon: Timer }
  );
}

export const MANUAL_ORIGIN_LABEL: Record<ManualOrigin, string> = {
  tramite: 'Trámite',
  prevalidacion: 'Prevalidación',
  mandatario: 'Mandatario',
  representante_legal: 'Representante legal',
};

export function manualOriginLabel(origin: string): string {
  return MANUAL_ORIGIN_LABEL[origin as ManualOrigin] ?? origin;
}

/** Minutos de espera → texto corto: «45 min», «2 h 05 min», «3 d 4 h». */
export function formatEspera(minutes: number): string {
  const m = Math.max(0, Math.floor(minutes));
  if (m < 1) return '< 1 min';
  if (m < 60) return `${m} min`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h} h ${String(m % 60).padStart(2, '0')} min`;
  return `${Math.floor(h / 24)} d ${h % 24} h`;
}
