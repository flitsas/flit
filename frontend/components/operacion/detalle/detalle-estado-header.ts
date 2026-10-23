import {
  AlertTriangle,
  Ban,
  Check,
  Clock,
  FileText,
  type LucideIcon,
} from 'lucide-react';
import type { InstanceStatus } from '@/lib/api/types/procedure-runtime';
import { estadoChipStyle, estadoLabel, estadoLabelConOrigen } from '@/lib/tramites/estados';

/** Chip sólido del header detalle (spec flit-detalle-tramite). */
export interface DetalleEstadoHeader {
  label: string;
  /** Tono del estado — mismo `accent` que fila/filtros (`estadoChipStyle`). */
  color: string;
  Icon: LucideIcon;
  /** Banner contextual opcional bajo el header. */
  alert: string | null;
  pendiente: boolean;
}

export function detalleEstadoHeader(
  estado: InstanceStatus,
  rejectedFrom: string | null | undefined = null,
): DetalleEstadoHeader {
  const color = estadoChipStyle(estado).accent;

  switch (estado) {
    case 'aprobado':
      return {
        label: estadoLabel(estado),
        color,
        Icon: Check,
        alert: null,
        pendiente: false,
      };
    case 'anulado':
      return {
        label: estadoLabel(estado),
        color,
        Icon: Ban,
        alert: 'Trámite anulado. Requiere radicación nueva si aplica.',
        pendiente: false,
      };
    case 'rechazado':
      return {
        // ADR-0059 — «Rechazado preasignación» cuando el OT rechazó desde la cola de placa.
        label: estadoLabelConOrigen(estado, rejectedFrom),
        color,
        Icon: Ban,
        alert:
          rejectedFrom === 'preasignacion'
            ? 'Trámite rechazado por el Organismo de Tránsito antes de asignar placa. Al subsanarlo y radicarlo de nuevo vuelve a la cola de placa.'
            : 'Trámite rechazado por el Organismo de Tránsito.',
        pendiente: false,
      };
    // Feature #12565 — el aviso se mantiene aquí (es el contrato de esta función, y hay quien la
    // usa sin el detalle a mano), pero `TramiteDetalleModal` lo suprime cuando
    // `lastRevocationDecision` ya anunció la revocatoria con su motivo y su fecha: si no, saldrían
    // dos avisos seguidos diciendo lo mismo.
    case 'revocado':
      return {
        label: estadoLabel(estado),
        color,
        Icon: Ban,
        alert: 'Aprobación revocada por el Organismo de Tránsito. La placa quedó liberada.',
        pendiente: false,
      };
    // ADR-0059 — ruta de placa: dos esperas distintas, y las dos se dicen.
    case 'preasignacion':
      return {
        label: estadoLabel(estado),
        color,
        Icon: Clock,
        alert: 'Radicado sin placa: el Organismo de Tránsito debe asignarla.',
        pendiente: true,
      };
    case 'asignado':
      return {
        label: estadoLabel(estado),
        color,
        Icon: Check,
        alert: 'Placa asignada por el Organismo de Tránsito. Gestiona el SOAT y los impuestos y envía el trámite al OT.',
        pendiente: true,
      };
    case 'borrador':
      return {
        label: estadoLabel(estado),
        color,
        Icon: FileText,
        alert: 'Trámite en borrador: faltan pasos por completar.',
        pendiente: false,
      };
    case 'entregado':
      return {
        label: estadoLabel(estado),
        color,
        Icon: Check,
        alert: null,
        pendiente: false,
      };
    case 'preparado':
      return {
        label: estadoLabel(estado),
        color,
        Icon: Clock,
        alert: null,
        pendiente: false,
      };
    case 'subsanacion':
      return {
        label: estadoLabel(estado),
        color,
        Icon: AlertTriangle,
        alert: 'Trámite en subsanación activa.',
        pendiente: true,
      };
    default:
      return {
        label: estadoLabel(estado),
        color,
        Icon: AlertTriangle,
        alert: 'Trámite pendiente por aprobación del Organismo de Tránsito.',
        pendiente: true,
      };
  }
}
