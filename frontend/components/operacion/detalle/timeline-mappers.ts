import type {
  BiometricEstado,
  BiometricParte,
  BiometricValidation,
  StatusHistory,
} from '@/lib/api/types/procedure-runtime';
import type { ProcedureFamily } from '@/lib/api/types/procedure-parametrization';
import { estadoChipStyle, estadoLabel } from '@/lib/tramites/estados';
import { formatFecha } from '@/lib/format/date';
import type { TimelineTrackNode } from './TimelineTrackPanel';

const GREEN = '#8CC63F';
const WARN = '#F9AC00';
const RED = '#FF4E00';
const GREY = '#94A3B8';
const BLUE = '#557EFF';

const PARTE_LABEL: Record<BiometricParte, string> = {
  vendedor: 'Vendedor',
  comprador: 'Comprador',
};

const ESTADO_COLOR: Record<BiometricEstado, string> = {
  aprobado: GREEN,
  en_proceso: WARN,
  enviado: BLUE,
  rechazado: RED,
  expirado: GREY,
  pendiente_envio: GREY,
  error_envio: RED,
};

const ESTADO_LABEL: Record<BiometricEstado, string> = {
  enviado: 'Enviado',
  en_proceso: 'En proceso',
  aprobado: 'Aprobado',
  rechazado: 'Rechazado',
  expirado: 'Expirado',
  pendiente_envio: 'Pendiente de envío',
  error_envio: 'Error de envío',
};

function hitoLabel(e: StatusHistory): string {
  const to = estadoLabel(e.toStatus);
  const from = e.fromStatus ? estadoLabel(e.fromStatus) : null;
  const reason = e.reason?.trim();
  return `${to}${from ? ` desde ${from}` : ''}${reason ? ` (${reason})` : ''}`;
}

/** Mapea `statusHistory` al patrón visual TimelineTrack del mockup. */
export function mapStatusHistoryToTimelineNodes(history: StatusHistory[]): TimelineTrackNode[] {
  const sorted = [...history].sort(
    (a, b) => new Date(a.changedAt).getTime() - new Date(b.changedAt).getTime(),
  );
  return sorted.map((e, i) => ({
    label: estadoLabel(e.toStatus),
    color: estadoChipStyle(e.toStatus).accent,
    info: {
      gestor: '—',
      correo: '—',
      empresa: '—',
      rol: 'Sistema',
      fecha: formatFecha(e.changedAt),
      extra: hitoLabel(e),
    },
    isActive: i === sorted.length - 1,
  }));
}

/** Mapea validaciones biométricas + firma del baúl (misma semántica que `TramiteDetalleIdentidad`). */
export function mapIdentidadToTimelineNodes(
  modalidad: ProcedureFamily,
  validations: BiometricValidation[],
  firmaBaulPartes: string[],
): TimelineTrackNode[] {
  const partes: BiometricParte[] = modalidad === 'TRASPASO' ? ['vendedor', 'comprador'] : ['comprador'];

  const nodes: TimelineTrackNode[] = partes.map((parte) => {
    const matches = validations.filter((v) =>
      modalidad === 'TRASPASO'
        ? v.partyRole === parte
        : v.partyRole === null || v.partyRole === 'comprador',
    );
    const ultima = matches.length > 0 ? matches[matches.length - 1]! : null;
    const enBaul = firmaBaulPartes.includes(parte);
    const label = PARTE_LABEL[parte];

    if (ultima) {
      const detalle =
        ultima.rejectionReason?.trim() ||
        ultima.ultimoIntentoMotivo?.trim() ||
        ESTADO_LABEL[ultima.status] ||
        ultima.status;
      return {
        label: `${label} · ${ESTADO_LABEL[ultima.status] ?? ultima.status}`,
        color: ESTADO_COLOR[ultima.status] ?? GREY,
        info: {
          gestor: ultima.name || '—',
          correo: ultima.email || '—',
          empresa: ultima.provider || 'Kyverum',
          rol: label,
          fecha: ultima.validatedAt
            ? formatFecha(ultima.validatedAt)
            : ultima.expiresAt
              ? formatFecha(ultima.expiresAt)
              : '—',
          extra: detalle,
        },
      };
    }

    if (enBaul) {
      return {
        label: `${label} · Firma del baúl`,
        color: GREEN,
        info: {
          gestor: '—',
          correo: '—',
          empresa: 'Baúl de firmas',
          rol: label,
          fecha: '—',
          extra: 'Acreditado por firma del baúl',
        },
      };
    }

    return {
      label: `${label} · Sin iniciar`,
      color: GREY,
      info: {
        gestor: '—',
        correo: '—',
        empresa: '—',
        rol: label,
        fecha: '—',
        extra: 'Validación de identidad no iniciada',
      },
    };
  });

  return nodes;
}
