'use client';

import { Bot, UserCheck } from 'lucide-react';
import { getToken } from '@/lib/api/client';
import { decodeJwtPayload, isSuperAdmin } from '@/lib/auth/jwt';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';

/**
 * Chip de «Origen de la aprobación» de una validación de identidad (Épica #13202, HU-C8): aprobada de forma automática o
 * por el proveedor, o revisada a mano por el Super Admin. Icono + texto, nunca solo color. Solo se pinta cuando el
 * origen existe; el nombre del revisor no se muestra en ningún caso.
 */
export type ApprovalOrigin = 'automatica' | 'manual';

const META: Record<ApprovalOrigin, { label: string; tone: StatusTone; Icon: typeof Bot }> = {
  automatica: { label: 'Automática', tone: 'neutral', Icon: Bot },
  manual: { label: 'Manual', tone: 'info', Icon: UserCheck },
};

/** «Manual» es del Super Admin FLIT: la compañía y el cliente no lo ven (ni el chip ni su rótulo). */
export function puedeVerOrigenAprobacion(origin: ApprovalOrigin | null | undefined): boolean {
  if (!origin) return false;
  return origin !== 'manual' || isSuperAdmin(decodeJwtPayload(getToken()));
}

export function ApprovalOriginChip({ origin }: { origin: ApprovalOrigin }) {
  const meta = META[origin];
  if (!meta) return null;
  if (!puedeVerOrigenAprobacion(origin)) return null;
  const { Icon } = meta;
  return (
    <StatusBadge
      tone={meta.tone}
      ariaLabel={`Origen de la aprobación: ${meta.label}`}
      label={
        <span className="inline-flex items-center gap-1.5">
          <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden />
          {meta.label}
        </span>
      }
    />
  );
}
