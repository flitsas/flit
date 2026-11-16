import type { ComponentType } from 'react';
import { iconoComo } from "@flit/ui/iconoComo";
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
  manual_activo: { label: 'Esperando captura', tone: 'info', icon: iconoComo("espera") },
  pendiente_revision_manual: { label: 'Pendiente de revisión', tone: 'info', icon: iconoComo("reloj") },
  aprobado: { label: 'Aprobada manual', tone: 'success', icon: iconoComo("check") },
  rechazado: { label: 'Rechazada', tone: 'danger', icon: iconoComo("cerrar") },
  expirado: { label: 'Vencida', tone: 'neutral', icon: iconoComo("cronometro") },
};

/** Estado desconocido (el backend puede añadir uno): neutral, con el código visible. */
export function manualStatusMeta(status: string): ManualStatusMeta {
  return (
    MANUAL_STATUS_META[status as ManualStatus] ?? { label: status, tone: 'neutral', icon: iconoComo("cronometro") }
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

/**
 * Minutos de revisión → texto corto: «45 min», «2 h 05 min», «3 d 4 h». `null` (la validación no está pendiente de
 * revisión: el backend solo mide la espera de la revisión) → «—».
 */
export function formatEspera(minutes: number | null | undefined): string {
  if (minutes === null || minutes === undefined) return '—';
  const m = Math.max(0, Math.floor(minutes));
  if (m < 1) return '< 1 min';
  if (m < 60) return `${m} min`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h} h ${String(m % 60).padStart(2, '0')} min`;
  return `${Math.floor(h / 24)} d ${h % 24} h`;
}

/** Motivo por el que una foto quedó «No verificada» (HU #14311), en lenguaje para quien revisa. */
const MOTIVO_FOTO: Record<string, string> = {
  sin_rostro: 'No se detectó un rostro.',
  varios_rostros: 'Se detectó más de una persona.',
  sin_documento: 'No se detectó un documento.',
  lado_incorrecto: 'Puede ser el lado equivocado del documento.',
  sin_soporte: 'El dispositivo no pudo validarla.',
  sin_resultado: 'No llegó el resultado de la validación.',
};

/** Texto del motivo; uno desconocido no rompe la pantalla. */
export function etiquetaMotivoFoto(motivo: string | null | undefined): string {
  return (motivo && MOTIVO_FOTO[motivo]) || 'Sin detalle del motivo.';
}
