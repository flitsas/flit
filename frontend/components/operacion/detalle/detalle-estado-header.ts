import {
  AlertTriangle,
  Ban,
  Check,
  Clock,
  FileText,
  type LucideIcon,
} from 'lucide-react';
import type { InstanceStatus } from '@/lib/api/types/procedure-runtime';
import { estadoLabel } from '@/lib/tramites/estados';
import {
  DETALLE_BLUE,
  DETALLE_GREEN,
  DETALLE_GREY,
  DETALLE_GOLD,
  DETALLE_RED,
} from './detalle-visual';

/** Chip sólido del header detalle (spec flit-detalle-tramite). */
export interface DetalleEstadoHeader {
  label: string;
  color: string;
  Icon: LucideIcon;
  /** Banner contextual opcional bajo el header. */
  alert: string | null;
  pendiente: boolean;
}

export function detalleEstadoHeader(estado: InstanceStatus): DetalleEstadoHeader {
  switch (estado) {
    case 'aprobado':
      return {
        label: estadoLabel(estado),
        color: DETALLE_GREEN,
        Icon: Check,
        alert: null,
        pendiente: false,
      };
    case 'anulado':
      return {
        label: estadoLabel(estado),
        color: DETALLE_RED,
        Icon: Ban,
        alert: 'Trámite anulado. Requiere radicación nueva si aplica.',
        pendiente: false,
      };
    case 'rechazado':
      return {
        label: estadoLabel(estado),
        color: DETALLE_RED,
        Icon: Ban,
        alert: 'Trámite rechazado por el Organismo de Tránsito.',
        pendiente: false,
      };
    case 'borrador':
      return {
        label: estadoLabel(estado),
        color: DETALLE_GREY,
        Icon: FileText,
        alert: 'Trámite en borrador: faltan pasos por completar.',
        pendiente: false,
      };
    case 'entregado':
      return {
        label: estadoLabel(estado),
        color: '#7C3AED',
        Icon: Check,
        alert: null,
        pendiente: false,
      };
    case 'preparado':
      return {
        label: estadoLabel(estado),
        color: DETALLE_BLUE,
        Icon: Clock,
        alert: null,
        pendiente: false,
      };
    case 'subsanacion':
      return {
        label: estadoLabel(estado),
        color: DETALLE_GOLD,
        Icon: AlertTriangle,
        alert: 'Trámite en subsanación activa.',
        pendiente: true,
      };
    default:
      return {
        label: estadoLabel(estado),
        color: DETALLE_GOLD,
        Icon: AlertTriangle,
        alert: 'Trámite pendiente por aprobación del Organismo de Tránsito.',
        pendiente: true,
      };
  }
}
