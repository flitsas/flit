import type {
  BiometricEstado,
  BiometricParte,
  BiometricValidation,
  ProcedureInstanceEvent,
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
          // Bug #12376, defecto 3 — correo del REGISTRO (inmutable), no el operativo: un reenvío
          // administrativo a otro correo no debe hacer que el tracking "olvide" el correo original.
          correo: ultima.registeredEmail || ultima.email || '—',
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

const PARTY_LABEL: Record<string, string> = {
  vendedor: 'Vendedor',
  comprador: 'Comprador',
};

/**
 * Bug #12376, defectos 3/4 — pinta los eventos administrativos (reenvío de validación, reasignación de
 * gestor) como nodos ADICIONALES del timeline: nunca reemplazan el nodo de identidad ni el de estado,
 * solo se agregan (mismo criterio que pide el bug: "queda como evento adicional, sin reemplazar el
 * histórico").
 */
export function mapEventsToTimelineNodes(events: ProcedureInstanceEvent[]): TimelineTrackNode[] {
  const sorted = [...events].sort(
    (a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime(),
  );

  return sorted.map((e) => {
    if (e.tipo === 'reasignar_gestor_admin') {
      return {
        label: 'Reasignación de gestor',
        color: BLUE,
        info: {
          gestor: e.newAssignedToName || '—',
          correo: '—',
          empresa: '—',
          rol: e.createdByName ? `Ejecutado por ${e.createdByName}` : 'Ejecutado por admin',
          fecha: formatFecha(e.createdAt),
          extra: `De ${e.previousAssignedToName || 'sin gestor asignado'} a ${e.newAssignedToName || '—'}`,
        },
      };
    }

    // reenvio_validacion_admin
    const parte = e.partyRole ? PARTY_LABEL[e.partyRole] ?? e.partyRole : null;
    return {
      label: `Reenvío de validación${parte ? ` · ${parte}` : ''}`,
      color: BLUE,
      info: {
        gestor: '—',
        correo: e.correoDestinoEnmascarado || '—',
        empresa: '—',
        rol: e.createdByName ? `Ejecutado por ${e.createdByName}` : 'Ejecutado por admin',
        fecha: formatFecha(e.createdAt),
        extra: e.emailActualizado
          ? 'Reenviado a un correo distinto del registrado'
          : 'Reenviado al correo actual',
      },
    };
  });
}
